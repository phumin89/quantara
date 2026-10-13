namespace Quantara.Portfolio.Domain;

public enum OrderState
{
    Pending,
    Expired,
    Rejected,
    Submitting,
    Submitted,
    Unknown,
}
