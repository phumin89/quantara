namespace Quantara.Portfolio.Application;

public sealed record RiskEvidenceOrigin(string Broker, string LegalRegion, string Environment, string AdapterVersion)
{
    public string Operation { get; } = "capitalEvidenceRead";
}
