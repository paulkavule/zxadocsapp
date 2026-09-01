using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using MudBlazor;
using zxadocsfe.Services;
using zxadocslib.Dtos;
using zxadocslib.Helpers;
using zxadocsui.State;

namespace zxadocsui.Components.Pages.Dashboard.Contracts;

// Raise and maintain a contract initiation request (ZD-120). One form for both routes: with an
// Id it loads and updates, without one it creates.
public partial class ContractRequestEditor
{
    [Parameter] public int Id { get; set; }

    [Inject] private IContractRequestClientService RequestsApi { get; set; } = default!;
    [Inject] private IContractTypeClientService TypesApi { get; set; } = default!;
    [Inject] private IPermissionClientService Permissions { get; set; } = default!;
    [Inject] private IUserSession Session { get; set; } = default!;
    [Inject] private ISnackbar Snackbar { get; set; } = default!;
    [Inject] private NavigationManager Nav { get; set; } = default!;
    [Inject] private ILogger<ContractRequestEditor> Logger { get; set; } = default!;

    private string reference = string.Empty;
    private string title = string.Empty;
    private string description = string.Empty;
    private string meetingNumber = string.Empty;
    private ContractNature nature = ContractNature.New;
    private int extendsRequestId;
    private int contractTypeId;
    private decimal amount;

    private List<ContractTypeDto> contractTypes = new();
    private List<ContractRequestDto> extendable = new();
    private List<ContractRequestFieldValueDto> fieldValues = new();
    private readonly List<ContractRequestKpiDto> kpis = new();
    private List<ContractRequestDocumentDto> documents = new();

    private bool loading = true;
    private bool saving;
    private bool canInitiate;
    private bool editable = true;

    // Supporting evidence is scanned paperwork; the server enforces the same ceiling.
    private const long MaxDocumentSize = 20 * 1024 * 1024;

    private bool IsNew => Id == 0;
    private bool ReadOnly => !canInitiate || !editable;

    /// <summary>Derived from award and expiry, which are contract type fields (ZD-117).</summary>
    private string Duration => DurationBetween(DateOf(SystemFieldKeys.Award), DateOf(SystemFieldKeys.Expiry));

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender) return;

        await Session.GetCurrentUser();
        var perms = await Permissions.GetPermissions();
        canInitiate = perms.Contains(Permission.InitiateContract);

        if (!perms.HasAnyContractPermission())
        {
            Snackbar.Add("You do not have permission to view contract requests.", Severity.Warning);
            Nav.NavigateTo("/dashboard");
            return;
        }
        if (IsNew && !canInitiate)
        {
            Snackbar.Add("You do not have permission to raise contract requests.", Severity.Warning);
            Nav.NavigateTo("/contract-requests");
            return;
        }

        var (typesOk, types, _) = await TypesApi.List();
        if (typesOk) contractTypes = types.ToList();

        var (listOk, existing, _) = await RequestsApi.List();
        if (listOk) extendable = existing.Where(r => r.Id != Id).ToList();

        if (!IsNew) await LoadRequest();
        loading = false;
        StateHasChanged();
    }

    private async Task LoadRequest()
    {
        var (ok, request, error) = await RequestsApi.Get(Id);
        if (!ok || request is null)
        {
            Snackbar.Add(error ?? "That contract request could not be found.", Severity.Warning);
            Nav.NavigateTo("/contract-requests");
            return;
        }

        reference = request.RequestReference;
        title = request.Title;
        description = request.Description;
        meetingNumber = request.MeetingNumber;
        nature = request.Nature;
        extendsRequestId = request.ExtendsRequestId ?? 0;
        contractTypeId = request.ContractTypeId;
        amount = request.Amount;
        editable = request.IsEditable;

        fieldValues = request.FieldValues.OrderBy(f => f.Order).ToList();
        kpis.Clear();
        kpis.AddRange(request.Kpis.OrderBy(k => k.Order));
        documents = request.Documents.ToList();
    }

    // Choosing a type loads the details it collects. Existing answers are kept where the key
    // matches, so a mis-click does not wipe what has been typed.
    private void OnContractTypeChanged()
    {
        var previous = fieldValues.ToDictionary(f => f.Key, f => f.Value, StringComparer.OrdinalIgnoreCase);
        var type = contractTypes.FirstOrDefault(t => t.Id == contractTypeId);

        fieldValues = type is null
            ? new List<ContractRequestFieldValueDto>()
            : type.Fields.OrderBy(f => f.Order).Select(f => new ContractRequestFieldValueDto
            {
                ContractTypeFieldId = f.Id,
                Key = f.Key,
                Label = f.Label,
                FieldType = f.FieldType,
                IsRequired = f.IsRequired,
                Options = f.Options,
                Order = f.Order,
                Value = previous.GetValueOrDefault(f.Key) ?? f.DefaultValue,
            }).ToList();
    }

    private void SetValue(ContractRequestFieldValueDto field, string? value) =>
        field.Value = value ?? string.Empty;

    private void AddKpi() => kpis.Add(new ContractRequestKpiDto { Order = kpis.Count + 1 });

    private async Task Save()
    {
        var problem = FirstProblem();
        if (problem is not null)
        {
            Snackbar.Add(problem, Severity.Warning);
            return;
        }

        saving = true;
        try
        {
            var request = new SaveContractRequestRequest
            {
                Title = title.Trim(),
                Description = description?.Trim() ?? string.Empty,
                MeetingNumber = meetingNumber?.Trim() ?? string.Empty,
                Nature = nature,
                ExtendsRequestId = nature == ContractNature.Extension && extendsRequestId > 0
                    ? extendsRequestId
                    : null,
                ContractTypeId = contractTypeId,
                Amount = amount,
                FieldValues = fieldValues,
                Kpis = kpis.Select((k, index) => k with { Order = index + 1 }).ToList(),
            };

            var (ok, saved, error) = IsNew
                ? await RequestsApi.Create(request)
                : await RequestsApi.Update(Id, request);

            if (!ok || saved is null)
            {
                Snackbar.Add(error ?? "Failed to save the contract request.", Severity.Error);
                return;
            }

            Snackbar.Add(IsNew ? $"{saved.RequestReference} raised." : "Contract request updated.", Severity.Success);

            // Documents attach to a request that exists, so a new one reopens on its own route.
            if (IsNew) Nav.NavigateTo($"/contract-requests/{saved.Id}");
            else Nav.NavigateTo("/contract-requests");
        }
        finally
        {
            saving = false;
        }
    }

    // Checked here so the operator is told which field is wrong; the server re-checks all of it.
    private string? FirstProblem()
    {
        if (string.IsNullOrWhiteSpace(title)) return "A contract request needs a title.";
        if (contractTypeId <= 0) return "Choose a contract type.";
        if (nature == ContractNature.Extension && extendsRequestId <= 0)
            return "Say which contract this one extends.";

        var missing = fieldValues.FirstOrDefault(f => f.IsRequired && string.IsNullOrWhiteSpace(f.Value));
        if (missing is not null) return $"{missing.Label} is required.";

        var award = DateOf(SystemFieldKeys.Award);
        var expiry = DateOf(SystemFieldKeys.Expiry);
        if (award is not null && expiry is not null && expiry <= award)
            return "The expiry date must be after the award date.";

        return null;
    }

    private async Task OnDocumentSelected(IBrowserFile? file)
    {
        if (file is null) return;
        if (file.Size > MaxDocumentSize)
        {
            Snackbar.Add($"{file.Name} is larger than 20 MB.", Severity.Warning);
            return;
        }

        try
        {
            using var stream = file.OpenReadStream(MaxDocumentSize);
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer);

            var (ok, added, error) = await RequestsApi.AddDocument(
                Id, buffer.ToArray(), file.Name, file.ContentType);
            if (!ok || added is null)
            {
                Snackbar.Add(error ?? "Could not attach the document.", Severity.Error);
                return;
            }

            documents.Add(added);
            Snackbar.Add($"{added.FileName} attached.", Severity.Success);
        }
        catch (Exception ex)
        {
            Logger.LogDebug("Document upload failed: {Message}", ex.Message);
            Snackbar.Add("Could not read the file. " + ex.Message, Severity.Error);
        }
    }

    private async Task RemoveDocument(ContractRequestDocumentDto document)
    {
        var (ok, error) = await RequestsApi.RemoveDocument(document.Id);
        if (!ok)
        {
            Snackbar.Add(error ?? "Could not remove the document.", Severity.Error);
            return;
        }
        documents.Remove(document);
    }

    // Both selects bind an int id, so without these they render the raw number.
    private string ContractTypeName(int id) => id == 0
        ? "Select a contract type"
        : contractTypes.FirstOrDefault(t => t.Id == id)?.Name ?? string.Empty;

    private string ExtendsLabel(int id) => id == 0
        ? "Select the contract being extended"
        : extendable.FirstOrDefault(r => r.Id == id)?.RequestReference ?? string.Empty;

    private static long Kilobytes(long bytes) => Math.Max(1, bytes / 1024);

    private DateTime? DateOf(string key)
    {
        var raw = fieldValues.FirstOrDefault(f =>
            string.Equals(f.Key, key, StringComparison.OrdinalIgnoreCase))?.Value;
        return DateTime.TryParse(raw, System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out var parsed) ? parsed : null;
    }

    /// <summary>Mirrors ContractRequestService so the caption matches what the server reports.</summary>
    private static string DurationBetween(DateTime? award, DateTime? expiry)
    {
        if (award is null || expiry is null || expiry <= award) return string.Empty;

        var months = ((expiry.Value.Year - award.Value.Year) * 12) + expiry.Value.Month - award.Value.Month;
        if (expiry.Value.Day < award.Value.Day) months--;
        var days = (expiry.Value - award.Value.AddMonths(months)).Days;

        var parts = new List<string>();
        if (months >= 12) parts.Add(Plural(months / 12, "year"));
        if (months % 12 > 0) parts.Add(Plural(months % 12, "month"));
        if (parts.Count == 0 || days > 0) parts.Add(Plural(days, "day"));
        return string.Join(" ", parts);
    }

    private static string Plural(int n, string unit) => $"{n} {unit}{(n == 1 ? "" : "s")}";

    private static class SystemFieldKeys
    {
        public const string Award = "award_date";
        public const string Expiry = "expiry_date";
    }
}
