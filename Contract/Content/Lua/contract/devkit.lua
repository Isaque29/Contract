-- Usage pattern: call register / create helpers, then `return devkit.buildManifest()`
-- Note: id parameters are REQUIRED and act as the canonical identifier for registry entries.

local devkit = {
    _version = "0.0.1",
    _rules = {
        gravity = 9.81,
        maxPlayers = 1,
        friendlyFire = false,
    },
    _chapters = {},     -- list of chapters
    _areas = {},        -- map id -> area
    _blocks = {},       -- map id -> block
    _entities = {},     -- map id -> entity
    _events = {},       -- map id -> handler (fn or descriptor)
    _diagnostics = { warns = {}, errors = {} },
    _denied = false,
    _enum = {},         -- storage for enums
    _camera = {},       -- camera defaults / config
    _assetResolver = nil -- optional user override for asset resolution
}

-- ========================
-- Utilities & Validation
-- ========================

function devkit.setVersion(v)
    devkit._version = tostring(v or devkit._version)
    return devkit._version
end

function devkit.getVersion()
    return devkit._version
end

local function assertId(id, what)
    if type(id) ~= "string" or id == "" then
        error(("%s must be a non-empty string id"):format(what or "object"))
    end
end

local function pushWarn(msg)
    table.insert(devkit._diagnostics.warns, tostring(msg))
end

local function pushError(msg)
    table.insert(devkit._diagnostics.errors, tostring(msg))
end

-- Public helpers requested by you
function devkit.addWarn(msg) pushWarn(msg) return msg end
function devkit.addError(msg) pushError(msg) return msg end

-- Deny the mod: prevents buildManifest from returning successfully.
-- The denial message and collected diagnostics will be included in the thrown error.
-- Use when a mod author introduced a fatal config/behavior that must stop loading.
function devkit.deny(reason)
    if reason then pushError(reason) end
    devkit._denied = true
end

function devkit.getDiagnostics()
    return {
        warns = devkit._diagnostics.warns,
        errors = devkit._diagnostics.errors,
        denied = devkit._denied
    }
end

function devkit.clearDiagnostics()
    devkit._diagnostics = { warns = {}, errors = {} }
    devkit._denied = false
end

-- ========================
-- Enum helper
-- ========================

-- Lua has no native enum. createEnum returns a table of constants and stores it at devkit._enum[name].
-- Example: local Difficulty = devkit.createEnum("Difficulty", {"a-side","b-side","c-side"})
function devkit.createEnum(name, list)
    if type(name) ~= "string" or name == "" then error("enum name must be string") end
    if type(list) ~= "table" then error("enum list must be table") end
    local t = {}
    for _, v in ipairs(list) do
        t[v] = v
    end
    devkit._enum[name] = t
    return t
end
function devkit.getEnum(name) return devkit._enum[name] end

-- ========================
-- Asset resolution
-- ========================

-- By default, resolve assets by category + id, e.g. "blocks/stone.png", "entities/npc_oldman.png".
-- You can override resolution by setting devkit.setAssetResolver(fn)
-- resolver signature: fn(category: string, id: string, hint?: string) -> string|nil
-- If resolver returns nil, devkit will use the default convention.
local function defaultResolveAsset(category, id, hint)
    if not id then return nil end
    
    -- allow hint to be a file extension suggestion or subpath
    -- default file layout conventions (modifiable)
    if category == "blocks" then
        return "blocks/" .. id .. ".png"
    elseif category == "entities" then
        return "entities/" .. id .. ".png"
    elseif category == "areas" then
        return "areas/" .. id .. "/bg.png"
    else
        return category .. "/" .. id .. ".png"
    end
end

function devkit.setAssetResolver(fn) devkit._assetResolver = fn end

function devkit.resolveAsset(category, id, hint)
    if devkit._assetResolver then
        local ok, out = pcall(devkit._assetResolver, category, id, hint)
        if ok and type(out) == "string" and out ~= "" then return out end
    end
    return defaultResolveAsset(category, id, hint)
end

-- Convenience: helper to get by id from any registry without path.
-- Example: devkit.get("blocks", "stone"), devkit.get("entities","npc_oldman")
function devkit.get(category, id)
    if not category or not id then return nil end
    if category == "blocks" then return devkit._blocks[id] end
    if category == "entities" then return devkit._entities[id] end
    if category == "areas" then return devkit._areas[id] end
    if category == "chapters" then
        for _, c in ipairs(devkit._chapters) do if c.id == id then return c end end
        return nil
    end
    return nil
end

-- ========================
-- Rules (global config)
-- ========================

function devkit.setRule(key, value) devkit._rules[key] = value return devkit._rules[key] end
function devkit.getRule(key) return devkit._rules[key] end

-- ========================
-- Chapters
-- ========================

-- registerChapter(opts) where opts.id is REQUIRED and acts as the canonical id for the chapter.
-- returned object has fields: id, name, description, difficulty, thumbnail, badge, areas(list)
function devkit.registerChapter(opts)
    opts = opts or {}
    assertId(opts.id, "Chapter")
    for _, c in ipairs(devkit._chapters) do if c.id == opts.id then pushWarn("chapter already registered: "..opts.id); return c end end
    local ch = {
        id = opts.id,
        name = opts.name or opts.id,
        description = opts.description or "",
        difficulty = opts.difficulty or "a-side",
        thumbnail = opts.thumbnail,
        badge = opts.badge,
        areas = opts.areas or {}
    }
    table.insert(devkit._chapters, ch)
    return ch
end

function devkit.addAreaToChapter(chapterId, areaId)
    assertId(chapterId,"Chapter id")
    assertId(areaId,"Area id")
    for _, ch in ipairs(devkit._chapters) do
        if ch.id == chapterId then
            for _, a in ipairs(ch.areas) do if a == areaId then return ch end end
            table.insert(ch.areas, areaId)
            return ch
        end
    end
    pushWarn("chapter not found: "..chapterId)
end

-- ========================
-- Blocks
-- ========================

-- registerBlock(id, opts)
-- id is REQUIRED and unique. opts: {name, asset (optional), behaviors = {onTouched, onUpdate, ...}, meta = {}}
function devkit.registerBlock(id, opts)
    assertId(id, "Block")
    opts = opts or {}
    if devkit._blocks[id] then pushWarn("block already registered: "..id); return devkit._blocks[id] end
    local blk = {
        id = id,
        name = opts.name or id,
        asset = opts.asset or devkit.resolveAsset("blocks", id, opts.assetHint),
        behaviors = opts.behaviors or {},
        meta = opts.meta or {}
    }
    devkit._blocks[id] = blk
    return blk
end

function devkit.unregisterBlock(id) devkit._blocks[id] = nil end
function devkit.listBlocks()
    local t = {}
    for k,v in pairs(devkit._blocks) do table.insert(t, v) end
    return t
end

-- ========================
-- Entities
-- ========================

-- registerEntity(id, opts)
-- opts: { name, asset, hitbox={w,h}, behavior = {onSpawn,onUpdate,onTouched,onPickup}, anim = {...}, meta = {} }
function devkit.registerEntity(id, opts)
    assertId(id,"Entity")
    opts = opts or {}
    if devkit._entities[id] then pushWarn("entity already registered: "..id); return devkit._entities[id] end
    local ent = {
        id = id,
        name = opts.name or id,
        asset = opts.asset or devkit.resolveAsset("entities", id, opts.assetHint),
        hitbox = opts.hitbox or {w=16,h=16},
        behavior = opts.behavior or {},
        anim = opts.anim,
        meta = opts.meta or {}
    }
    devkit._entities[id] = ent
    return ent
end

function devkit.unregisterEntity(id) devkit._entities[id] = nil end
function devkit.listEntities()
    local t = {}
    for k,v in pairs(devkit._entities) do table.insert(t, v) end
    return t
end

-- =================================
-- Areas (maps) - grid helpers
-- =================================

-- createArea(id, opts)
-- opts may include layers (parallax), width/height, defaultBlock, grid (table), camera hints, meta
function devkit.createArea(id, opts)
    assertId(id,"Area")
    opts = opts or {}
    if devkit._areas[id] then pushWarn("area already exists: "..id); return devkit._areas[id] end
    local area = {
        id = id,
        name = opts.name or id,
        description = opts.description or "",
        thumbnail = opts.thumbnail or devkit.resolveAsset("areas", id),
        layers = opts.layers or {}, -- { {path=..., parallax=0.2}, ... }
        width = opts.width or 100,
        height = opts.height or 50,
        defaultBlock = opts.defaultBlock or "air",
        grid = opts.grid, -- if nil, create on demand
        blocks = {},
        entities = {}, -- { {id = entityId, x=.., y=.., props = {} }, ... }
        meta = opts.meta or {},
        camera = opts.camera or {}
    }
    if not area.grid then
        area.grid = {}
        for y = 1, area.height do
            area.grid[y] = {}
            for x = 1, area.width do
                area.grid[y][x] = area.defaultBlock
            end
        end
    end
    devkit._areas[id] = area
    return area
end

-- set single block at (x,y)
function devkit.areaSetBlock(areaId, x, y, blockId)
    assertId(areaId, "Area id")
    assertId(blockId, "Block id")
    local area = devkit._areas[areaId]
    if not area then error("Area not found: "..tostring(areaId)) end
    if x < 1 or y < 1 or x > area.width or y > area.height then error("Coordinate out of range: "..x..","..y) end
    area.grid[y][x] = blockId
    -- register block reference if not already present
    local seen = false
    for _, b in ipairs(area.blocks) do if b == blockId then seen = true; break end end
    if not seen then table.insert(area.blocks, blockId) end
end

-- fill rectangular region with a block
function devkit.areaFillRegion(areaId, x1, y1, x2, y2, blockId)
    assertId(areaId,"Area id"); assertId(blockId,"Block id")
    local area = devkit._areas[areaId]
    if not area then error("Area not found: "..tostring(areaId)) end
    for y = math.max(1,y1), math.min(area.height,y2) do
        for x = math.max(1,x1), math.min(area.width,x2) do
            area.grid[y][x] = blockId
        end
    end
    local seen = false
    for _, b in ipairs(area.blocks) do if b == blockId then seen = true; break end end
    if not seen then table.insert(area.blocks, blockId) end
end

-- place an entity instance in the area
function devkit.areaPlaceEntity(areaId, entityId, x, y, props)
    assertId(areaId,"Area id"); assertId(entityId,"Entity id")
    local area = devkit._areas[areaId]
    if not area then error("Area not found: "..tostring(areaId)) end
    local inst = { id = entityId, x = x or 1, y = y or 1, props = props or {} }
    table.insert(area.entities, inst)
    return inst
end

function devkit.listAreas()
    local out = {}
    for k,v in pairs(devkit._areas) do table.insert(out, v) end
    return out
end

-- =================================
-- Events & Triggers
-- =================================

-- registerEvent(id, handler) where handler may be function or descriptor string.
function devkit.registerEvent(id, handler)
    assertId(id,"Event")
    if type(handler) ~= "function" and type(handler) ~= "string" then error("Event handler must be function or string") end
    devkit._events[id] = handler
    return handler
end

function devkit.triggerEvent(id, ...)
    local h = devkit._events[id]
    if not h then pushWarn("event not found: "..tostring(id)); return end
    if type(h) == "function" then
        local ok, err = pcall(h, ...)
        if not ok then pushError("event '"..id.."' error: "..tostring(err)) end
    else
        -- if string descriptor, engine will act on it during runtime.
        -- keep as-is in manifest
    end
end

-- ========================
-- Camera & Platformer helpers
-- ========================

-- Set default camera settings suitable for side-scrolling platformers.
-- cameraConfig example: { followDeadzone = {w=48,h=32}, lookahead = 40, verticalClamp = {min=-50,max=50}, smoothing = 0.15 }
function devkit.setCameraConfig(cameraConfig)
    devkit._camera = cameraConfig or {}
end
function devkit.getCameraConfig() return devkit._camera end

-- ========================
-- Grid compression helpers (RLE)
-- Useful to reduce manifest size when grids are huge.
-- ========================

function devkit.compressGrid_RLE(grid)
    -- returns a simple RLE representation: { width = w, height = h, rows = { {value, count, value, count, ...}, ... } }
    local w = #grid[1]
    local h = #grid
    local rows = {}
    for y=1,h do
        local r = grid[y]
        local out = {}
        local current = r[1]; local count = 1
        for x=2,w do
            if r[x] == current then
                count = count + 1
            else
                table.insert(out, current); table.insert(out, count)
                current = r[x]; count = 1
            end
        end
        table.insert(out, current); table.insert(out, count)
        table.insert(rows, out)
    end
    return { width = w, height = h, rows = rows }
end

function devkit.expandGrid_RLE(rle)
    local w = rle.width; local h = rle.height
    local grid = {}
    for y=1,h do
        local rowSpec = rle.rows[y]
        grid[y] = {}
        local x = 1
        for i=1,#rowSpec,2 do
            local val = rowSpec[i]; local cnt = rowSpec[i+1]
            for k=1,cnt do grid[y][x] = val; x = x + 1 end
        end
    end
    return grid
end

-- ========================
-- Manifest builder
-- ========================

-- buildManifest() returns a table ready to be returned by /Lua/manifest.lua
-- If devkit._denied == true buildManifest will throw with collected diagnostics.
function devkit.buildManifest()
    -- if a fatal deny occurred, raise with diagnostics so Loader (C#) will stop load and display messages
    if devkit._denied then
        local lines = {}
        table.insert(lines, "MOD DENIED: Fatal errors detected.")
        if #devkit._diagnostics.errors > 0 then
            table.insert(lines, "Errors:")
            for _, e in ipairs(devkit._diagnostics.errors) do table.insert(lines, "  - "..e) end
        end
        if #devkit._diagnostics.warns > 0 then
            table.insert(lines, "Warnings:")
            for _, w in ipairs(devkit._diagnostics.warns) do table.insert(lines, "  - "..w) end
        end
        error(table.concat(lines, "\n"))
    end

    local manifest = {
        version = devkit._version,
        configs = {
            rules = devkit._rules,
            chapters = devkit._chapters,
            player = {
                defaultHealth = 100,
                defaultSpeed = 200
            },
            env = {},
            areas = {},
            entities = {},
            blocks = {},
            events = devkit._events,
            camera = devkit._camera,
            diagnostics = devkit.getDiagnostics()
        }
    }

    -- populate areas keyed by id
    for id, area in pairs(devkit._areas) do
        -- fill missing thumbnails/assets using resolver
        if not area.thumbnail then area.thumbnail = devkit.resolveAsset("areas", id) end
        manifest.configs.areas[id] = {
            id = area.id,
            name = area.name,
            description = area.description,
            thumbnail = area.thumbnail,
            layers = area.layers,
            width = area.width,
            height = area.height,
            defaultBlock = area.defaultBlock,
            blocks = area.blocks,
            entities = area.entities,
            meta = area.meta,
            grid = area.grid -- note: engine may want RLE to be stored instead; both supported
        }
    end

    -- blocks & entities: keyed maps
    for id, b in pairs(devkit._blocks) do
        if not b.asset then b.asset = devkit.resolveAsset("blocks", id) end
        manifest.configs.blocks[id] = b
    end
    for id, e in pairs(devkit._entities) do
        if not e.asset then e.asset = devkit.resolveAsset("entities", id) end
        manifest.configs.entities[id] = e
    end

    -- chapters already in list
    manifest.configs.chapters = devkit._chapters

    return manifest
end

-- Reset helper (dev convenience)
function devkit.reset()
    devkit._rules = { gravity = 9.81, maxPlayers = 1, friendlyFire = false }
    devkit._chapters = {}
    devkit._areas = {}
    devkit._blocks = {}
    devkit._entities = {}
    devkit._events = {}
    devkit.clearDiagnostics()
    devkit._camera = {}
end

-- Convenience registry shorthand (functional style)
devkit.register = {
    block = devkit.registerBlock,
    entity = devkit.registerEntity,
    area = devkit.createArea,
    chapter = devkit.registerChapter,
    event = devkit.registerEvent,
    rule = devkit.setRule
}

return devkit
