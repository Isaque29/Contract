local devkit = require("contract.devkit")

devkit.setVersion("0.1.0")
devkit.createEnum("Difficulty", {"a-side","b-side","c-side"})

devkit.setCameraConfig{ followDeadzone = {w=48,h=28}, lookahead = 40, smoothing = 0.12 }

devkit.registerBlock("stone", { name = "Stone" })
devkit.registerBlock("spike", { name = "Spike", behaviors = { onTouched = function(self, who)
    print("ouch") end }}
)

local a = devkit.createArea("intro_field", { name = "Starting Field", width = 80, height = 20, defaultBlock = "air" })
devkit.areaFillRegion("intro_field", 1, 18, 80, 20, "stone")
devkit.areaPlaceEntity("intro_field", "old_man", 12, 17, { dialogue = "Welcome!" })


return devkit.buildManifest()
