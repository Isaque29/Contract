using Microsoft.Xna.Framework;
using Contract.Lib;
namespace Contract.Core;

public class ContractGame : Game
{
    private GraphicsDeviceManager _graphics;

    private readonly int _screenWidth = 1280;
    private readonly int _screenHeight = 720;

    public ContractGame()
    {
        _graphics = new GraphicsDeviceManager(this);
        Content.RootDirectory = "Content";
        IsMouseVisible = true;

        _graphics.PreferredBackBufferWidth = _screenWidth;
        _graphics.PreferredBackBufferHeight = _screenHeight;
    }

    protected override void Initialize()
    {
        Loader.Initialize();
        base.Initialize();
    }

    protected override void LoadContent()
    {
    }

    protected override void Update(GameTime gameTime)
    {
        InputManager.Update();
        if (InputManager.IsCancelPressed())
            Exit();
    }

    protected override void Draw(GameTime gameTime)
    {
    }
}
