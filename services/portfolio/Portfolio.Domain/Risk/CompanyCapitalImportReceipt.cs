namespace Quantara.Portfolio.Domain.Risk;

public sealed record CompanyCapitalImportReceipt(CompanyCapitalImport Import, string Digest, DateTimeOffset RecordedAt);
