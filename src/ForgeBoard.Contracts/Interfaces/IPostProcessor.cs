namespace ForgeBoard.Contracts.Interfaces;

public interface IPostProcessor
{
    string Name { get; }

    Task ProcessAsync(
        string inputPath,
        string outputPath,
        Action<string> log,
        CancellationToken ct
    );
}
