namespace Finex.API.Contracts.Production
{
    public sealed record RejectShiftReportRequest(
     string RejectionReason,
     string ReviewedBy);
}