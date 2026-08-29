namespace zxadocsfe.Dtos;

/// <summary>
/// What POST /api/users returns (ZD-111). Declared here rather than in pkavule.zxadocslib so the
/// contract change needs no package version bump; the shape must match the API's CreatedUser.
/// </summary>
public record CreatedUser
{
    public int Id { get; set; }

    /// <summary>The handle POST /api/users/signature is keyed on.</summary>
    public Guid UserReference { get; set; }
}
