namespace zxadocsfe.Dtos;

/// <summary>
/// A user as the API returns it (ZD-114): the shared contract plus the reference the signature
/// endpoints are keyed on. Declared here rather than in pkavule.zxadocslib so the edit screen
/// needs no package bump; the shape must match the API's UserSummary.
/// </summary>
public record UserSummary : zxadocslib.Dtos.User
{
    public Guid UserReference { get; set; }
}
