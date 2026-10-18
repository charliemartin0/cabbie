namespace Cabbie.Transports.Contracts;

/// <summary>The body every contract envelope carries. Public so System.Text.Json can bind it.</summary>
public sealed record ContractMessage(int Number, string Text);
