using GettingStarted;

IReadOnlyList<int> placed = await GettingStartedHost.RunAsync();
Console.WriteLine($"OrderPlaced handled for: {string.Join(", ", placed)}");
