using zxadocsfe.Services;
using zxadocslib.Dtos;

namespace zxadocsui.State;

// Carries the generate-for-signing payload from Draft Detail to the /createdocument wizard
// across navigation (FR-F8). Scoped per user/circuit; consumed once by CreateWorkflow so the
// handed-off PDF is picked up without a re-upload.
public class DraftHandoffState : IScopedUserState
{
    public GenerateForSigningResponse? Payload { get; private set; }

    // The originating draft, needed to fetch the rendered PDF via api/drafts/{id}/download.
    // Carried here rather than on the response DTO so the shared contract stays unchanged.
    public int DraftId { get; private set; }

    public void Set(GenerateForSigningResponse payload, int draftId)
    {
        Payload = payload;
        DraftId = draftId;
    }

    // An un-consumed handoff is one user's generated document; it must never be picked up by
    // the next person to sign in on this circuit.
    public void ClearUserState()
    {
        Payload = null;
        DraftId = 0;
    }

    public (GenerateForSigningResponse? Payload, int DraftId) Consume()
    {
        var p = Payload;
        var id = DraftId;
        Payload = null;
        DraftId = 0;
        return (p, id);
    }
}
