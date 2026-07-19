using zxadocslib.Dtos;

namespace zxadocsui.State;

// Carries the generate-for-signing payload from Draft Detail to the /createdocument wizard
// across navigation (FR-F8). Scoped per user/circuit; consumed once by CreateWorkflow so the
// handed-off PDF is picked up without a re-upload.
public class DraftHandoffState
{
    public GenerateForSigningResponse? Payload { get; private set; }

    public void Set(GenerateForSigningResponse payload) => Payload = payload;

    public GenerateForSigningResponse? Consume()
    {
        var p = Payload;
        Payload = null;
        return p;
    }
}
