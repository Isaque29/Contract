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
        Content.RootDirectory = "Mod";
        IsMouseVisible = true;

        _graphics.PreferredBackBufferWidth = _screenWidth;
        _graphics.PreferredBackBufferHeight = _screenHeight;
    }

    protected override void Initialize()
    {
        Loader.Initialize(Content.RootDirectory);
        base.Initialize();

        string v = Loader.Load<string>("version");
        Console.WriteLine($"Loading Contract with version {v}");
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
