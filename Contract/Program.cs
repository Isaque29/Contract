using Contract.Core;
namespace Contract;

public class Program
{
    public static void Main()
    {
        using var game = new ContractGame();
        game.Run();
    }
}
