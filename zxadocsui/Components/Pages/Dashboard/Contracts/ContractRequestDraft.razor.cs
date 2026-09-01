using Microsoft.AspNetCore.Components;
using MudBlazor;
using zxadocsfe.Services;
using zxadocslib.Dtos;
using zxadocslib.Helpers;
using zxadocsui.Components.Pages.Dashboard.Templates;
using zxadocsui.State;

namespace zxadocsui.Components.Pages.Dashboard.Contracts;

// Raise a draft against a contract request (ZD-121). Offers only templates of the request's
// contract type — a template of another type could not merge these values. The list sits beside
// a live preview of the selected template, with the first selected on arrival.
public partial class ContractRequestDraft
{
    [Parameter] public int Id { get; set; }

    [Inject] private IContractRequestClientService RequestsApi { get; set; } = default!;
    [Inject] private ITemplateClientService TemplatesApi { get; set; } = default!;
    [Inject] private IPermissionClientService Permissions { get; set; } = default!;
    [Inject] private IUserSession Session { get; set; } = default!;
    [Inject] private ISnackbar Snackbar { get; set; } = default!;
    [Inject] private NavigationManager Nav { get; set; } = default!;

    private string reference = string.Empty;
    private string contractTypeName = string.Empty;
    private List<TemplateDto> templates = new();
    private bool loading = true;
    private bool busy;
    private bool previewing;

    // Nothing is created until the selected template is confirmed.
    private TemplateDto? selected;
    private string previewHtml = string.Empty;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender) return;

        await Session.GetCurrentUser();
        var perms = await Permissions.GetPermissions();
        if (!perms.Contains(Permission.CreateDraft) || !perms.HasAnyContractPermission())
        {
            Snackbar.Add("You do not have permission to draft contracts.", Severity.Warning);
            Nav.NavigateTo("/contract-requests");
            return;
        }

        var (ok, request, error) = await RequestsApi.Get(Id);
        if (!ok || request is null)
        {
            Snackbar.Add(error ?? "That contract request could not be found.", Severity.Warning);
            Nav.NavigateTo("/contract-requests");
            return;
        }
        reference = request.RequestReference;
        contractTypeName = request.ContractTypeName;

        // From the request endpoint, which already filters to approved templates of its type.
        // /api/templates would 403 a drafter who does not also hold ViewTemplates.
        var (listOk, offered, listError) = await RequestsApi.DraftableTemplates(Id);
        if (listOk) templates = offered.ToList();
        // Reported, not returned on: the page still has to stop saying "Loading".
        else Snackbar.Add(listError ?? "Could not load the templates for this contract type.", Severity.Error);

        loading = false;
        StateHasChanged();

        // The common case is one template, so arriving at an empty pane and having to click is
        // pure friction; previewing creates nothing.
        if (templates.Count > 0) await Select(templates[0]);
    }

    private bool IsSelected(TemplateDto template) => selected?.Id == template.Id;

    private async Task Select(TemplateDto template)
    {
        selected = template;
        previewing = true;
        previewHtml = string.Empty;
        StateHasChanged();
        try
        {
            var (ok, html, error) = await RequestsApi.PreviewWithTemplate(Id, template.Id);
            // Stored image URLs are canonical and unsigned; the preview iframe resolves them
            // against the UI's own origin and 404s, so they must be signed before display.
            previewHtml = ok ? await TemplateHtml.WithDisplayableImagesAsync(html, TemplatesApi) : string.Empty;
            // A DOCX or PDF template has no HTML to show; it can still be used, so this is a
            // note on the preview rather than a refusal.
            if (!ok && !string.IsNullOrWhiteSpace(error)) Snackbar.Add(error, Severity.Info);
        }
        finally
        {
            previewing = false;
            StateHasChanged();
        }
    }

    private async Task Raise(TemplateDto template)
    {
        busy = true;
        try
        {
            var (ok, draft, error) = await RequestsApi.RaiseDraft(Id, template.Id);
            if (!ok || draft is null)
            {
                Snackbar.Add(error ?? "Could not raise the draft.", Severity.Error);
                return;
            }

            Snackbar.Add($"Draft raised from {reference}.", Severity.Success);
            // The editor, not /drafts/{id}: that route is the read-only detail page, and the
            // drafter has come here to write and submit the contract.
            Nav.NavigateTo($"/drafts/{draft.Id}/edit");
        }
        finally
        {
            busy = false;
        }
    }
}
