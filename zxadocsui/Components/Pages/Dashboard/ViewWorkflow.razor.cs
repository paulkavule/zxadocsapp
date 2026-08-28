using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using MudBlazor;
using Newtonsoft.Json;
using zxadocsfe.Dtos;
using zxadocsfe.Helpers;
using zxadocsfe.Services;
using zxadocslib.Dtos;
using zxadocslib.Helpers;
using zxadocsui.Components.DocWorkflow;
using zxadocsui.State;

namespace zxadocsui.Components.Pages.Dashboard;

public partial class ViewWorkflow
{
    [Inject] ISnackbar? Snackbar { get; set; } = default;
    [Inject] ILogger<ViewWorkflow>? logger { set; get; }
    [Inject] private IHttpService httpSvc { get; set; } = default!;
    [Inject] private NavigationManager navManager { get; set; } = default!;
    [Inject] private IUserSession session { get; set; } = default!;
    [Parameter] public string DocId { set; get; } = string.Empty;
    private DocumentEditor? childRef;
    string fileBase64 = string.Empty, docType = string.Empty, userName = string.Empty, fileName = "File Name",
    docRef = string.Empty, organisationId = string.Empty;
    private double UploadProgress { get; set; }
    private ListOption approvalStatus = new ListOption { Id = 0, Name = "Select" };
    private List<ListOption> priorityList = new(), doctypeList = new(), docCatList = new(), usersList = new(), docStatusList = new(), workflowList = new();
    private List<DocumentWorkflow> docWorkflowList = new();
    private List<DocAttachment> attachmentList = new();
    private List<DocCategoryField> extraFields = new();

    Document document = new();

    UserData userData = new UserData();
    // private byte[] fileBytes = default!;

    private ListOption ForwardTo = new();
    bool editMode, success, disableEdits;
    string[] errors = { };
    // string[] errors = { };

    // The list-options endpoint reads the organisation from the route and never falls back to the
    // token, so a hardcoded id here serves org 1's values to every tenant.
    private int OrgId => int.TryParse(userData.OrgId, out var value) ? value : 0;
    protected override async Task OnInitializedAsync()
    {

        httpSvc!.Initialize(AppConstants.HttpSchemes.Core);

    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            userData = await session.GetCurrentUser();
            if (string.IsNullOrWhiteSpace(DocId) == false)
            {
                editMode = true;
                await FeatchCustomWorkflow(DocId);
                await FetchDocumentDetails(DocId);
            }
            docStatusList = Enum.GetValues<ApprovalStatus>().Select(e => new ListOption { Id = (int)e, Name = e.ToString() }).ToList();
            await loadPriorities();
            await loadDocumentTypes();
            StateHasChanged();
        }
    }

    private async Task FeatchCustomWorkflow(string docId)
    {
        try
        {
            var (status, result, message) = await httpSvc!.ExecuteRequestAsync<ApiResponse<List<DocumentWorkflow>>>(HttpVerb.Get, $"api/docworkflow/{docId}");
            if (status == false || result?.Data == null)
            {
                //show dialog at this point
                Snackbar?.Clear();
                Snackbar?.Add(message!, Severity.Normal);
                return;
            }

            docWorkflowList = result.Data;
        }
        catch (Exception ee)
        {
            logger!.LogError(ee, ee.Message);
        }
    }

    public async Task FetchDocumentDetails(string docId)
    {
        try
        {
            var (status, result, message) = await httpSvc!.ExecuteRequestAsync<ApiResponse<List<Document>>>(HttpVerb.Get, $"api/documents/{docId}");
            if (status == false || result?.Data == null)
            {
                //show dialog at this point
                Snackbar?.Clear();
                Snackbar?.Add(message!, Severity.Normal);
                return;
            }
            document = result?.Data?.Where(dd => dd.Id == int.Parse(docId)).FirstOrDefault()!;

            disableEdits = userData.UserId != document.NextActor || document.Status == DocStatus.Archived;
            await loadPriorities();
            await loadDocumentTypes();
            await DocTypeChanged(document.TypeId + "");
            await DocCategoryChanged(document.CategoryId + "");

            //extraFields = document.ExtraFields.Select(dd => new DocCategoryField { FieldId = dd.FieldId, FieldName = dd., SelectedValue = dd.FieldValue }).ToList();
            // Snackbar?.Clear();
            // Snackbar?.Add("Downloading file. Please wait....", Severity.Info);
            // fileName = $"doc_{docId}.pdf";
            // await using var fileStream = File.Create(fileName);

            // await foreach (var chunk in httpSvc.DownloadDocumentFileAsync(docId: int.Parse(docId)))
            //     await fileStream.WriteAsync(chunk, 0, chunk.Length);


            // Snackbar?.Clear();
            // Snackbar?.Add("Downloading complete. Please proceed....", Severity.Info);

            // fileStream.Flush();
            // fileStream.Close();

            // fileBase64 = Convert.ToBase64String(File.ReadAllBytes(fileName));

            // StateHasChanged();
        }
        catch (Exception ex)
        {
            Snackbar?.Clear();
            Snackbar?.Add("An error occured while fetching document details. " + ex.Message, Severity.Error);
            logger!.LogDebug(ex.Message);
        }
    }
    // protected override async Task OnAfterRenderAsync(bool firstRender)
    // {
    //     if (firstRender)
    //     {
    //         await loadPriorities();
    //         await loadDocumentTypes();
    //     }
    // }

    private async Task loadPriorities()
    {
        try
        {
            var (status, result, message) = await httpSvc!.GetAsync<ApiResponse<List<ListOption>>>($"api/listoptions/{OrgId}?type=Priority");
            if (status == false || result?.Data == null)
            {
                //show dialog at this point
                Snackbar?.Clear();
                Snackbar?.Add(message!, Severity.Normal);
                return;
            }

            priorityList = result.Data;
        }
        catch (Exception ex)
        {
            logger!.LogDebug(ex.Message);
        }
    }

    private async Task loadDocumentTypes()
    {
        try
        {
            var (status, result, message) = await httpSvc!.GetAsync<ApiResponse<List<ListOption>>>($"api/listoptions/{OrgId}?type=documenttype");
            if (status == false || result?.Data == null)
            {
                //show dialog at this point
                return;
            }

            doctypeList = result.Data;
        }
        catch (Exception ex)
        {
            logger!.LogDebug(ex.Message);
        }
    }

    private async Task DocTypeChanged(string value)
    {
        try
        {
            if (value == "0")
                return;
            document.TypeId = int.Parse(value);
            var (status, result, message) = await httpSvc!.GetAsync<ApiResponse<List<ListOption>>>($"api/listoptions/{OrgId}?type=documentcategory&category={value}");
            if (status == false || result?.Data == null)
            {
                //show dialog at this point
                Snackbar!.Add(message!, Severity.Error);
                return;
            }

            docCatList = result.Data;
        }
        catch (Exception ex)
        {
            logger!.LogDebug(ex.Message);
        }
    }
    private async Task DocCategoryChanged(string value)
    {
        try
        {
            document.CategoryId = int.Parse(value);
            var (exists, data, message) = await httpSvc!.GetAsync<ApiResponse<List<DocumentCategory>>>($"api/doccategory/{value}");
            if (!exists)
            {
                Snackbar!.Add("Selected document category does not exist" + message, Severity.Error);
                return;
            }

            extraFields = data?.Data[0].ExtraFields.Select((dd) =>
            {
                if (editMode)
                {
                    var matchingField = document?.ExtraFields?.Where(def => def.FieldId == dd.FieldId).FirstOrDefault();
                    if (matchingField != null)
                    {
                        return new DocCategoryField
                        {
                            FieldName = dd.FieldName,
                            FieldId = dd.FieldId,
                            Options = dd.Options,
                            CategoryId = dd.CategoryId,
                            SelectedValue = matchingField.FieldValue
                        };
                    }
                }
                return new DocCategoryField
                {
                    FieldName = dd.FieldName,
                    FieldId = dd.FieldId,
                    Options = dd.Options,
                    CategoryId = dd.CategoryId
                };
            }).ToList() ?? new List<DocCategoryField>();

            (exists, var result, message) = await httpSvc!.GetAsync<ApiResponse<List<ListOption>>>($"api/listoptions/{OrgId}?type=documentworkflow&category={value}");
            if (!exists)
                usersList = result?.Data ?? new List<ListOption>();
            //show dialog at this point



        }
        catch (Exception ex)
        {
            logger!.LogDebug(ex.Message);
        }
    }

    private async Task OnForwardToChanged(ListOption option) => document.NextActor = option.Id + "";
    // private async Task UploadFileDocument(IBrowserFile file)
    // {
    //     try
    //     {
    //         var buffer = new byte[4096];
    //         long totalBytes = file.Size;
    //         long bytesRead = 0;
    //         Console.WriteLine($"Starting upload of file: {file.Name}, Size: {totalBytes} bytes");
    //         // Create a new progress object to report upload progress
    //         var progress = new Progress<double>(percentage =>
    //         {
    //             // This will be called as the upload progresses
    //             UploadProgress = percentage;
    //             StateHasChanged();
    //         });

    //         Console.WriteLine($"File size: {file.Size}\n");
    //         fileName = file.Name;
    //         var stream = file.OpenReadStream(maxAllowedSize: 10485760);
    //         using var ms = new MemoryStream();
    //         int counter = 0;
    //         while (await stream.ReadAsync(buffer) is int read && read > 0)
    //         {
    //             await ms.WriteAsync(buffer.AsMemory(0, read));
    //             bytesRead += read;
    //             Console.WriteLine($"Reading bytes {counter}");
    //             // Calculate and report progress
    //             var percentage = (double)bytesRead / totalBytes * 100;

    //             // Console.WriteLine($"Upload progress: {percentage}%");
    //             ((IProgress<double>)progress).Report(percentage);
    //             await Task.Delay(20);
    //             counter++;
    //         }
    //         fileBytes = ms.ToArray();
    //         fileBase64 = Convert.ToBase64String(fileBytes);
    //         Console.WriteLine($"string length from uploadfiledocument is {fileBase64.Length}");
    //         StateHasChanged();
    //     }
    //     catch (Exception ee)
    //     {
    //         logger!.LogDebug(ee.StackTrace);
    //     }
    // }
    private async Task UploadDocument(InputFileChangeEventArgs e)
    {
        var file = e.File;
        var buffer = new byte[4096];
        long totalBytes = file.Size;
        long bytesRead = 0;
        Console.WriteLine($"Starting upload of file: {file.Name}, Size: {totalBytes} bytes");
        // Create a new progress object to report upload progress
        var progress = new Progress<double>(percentage =>
        {
            // This will be called as the upload progresses
            UploadProgress = percentage;
            StateHasChanged();
        });

        try
        {
            // Create a new memory stream to store the file
            using var stream = file.OpenReadStream(maxAllowedSize: 10485760); // 10MB max
            using var ms = new MemoryStream();

            while (await stream.ReadAsync(buffer) is int read && read > 0)
            {
                await ms.WriteAsync(buffer.AsMemory(0, read));
                bytesRead += read;

                // Calculate and report progress
                var percentage = (double)bytesRead / totalBytes * 100;

                // Console.WriteLine($"Upload progress: {percentage}%");
                ((IProgress<double>)progress).Report(percentage);
                await Task.Delay(10);
            }
            ms.ToArray();
            fileBase64 = Convert.ToBase64String(ms.ToArray());
            Console.WriteLine($"string length from uploaddocument is {fileBase64.Length}");


        }
        catch (Exception ex)
        {

        }
    }

    private void InitializeDocumentAttachements(List<DocAttachment> attachments)
    {
        attachmentList = attachments;
        Console.WriteLine($"InitializeDocumentAttachements <~><~><~><~><~> {attachments?.Count}");
    }
    void ApprovalActionSelected(ListOption option) => approvalStatus = option;
    private async Task SubmitDocument()
    {
        try
        {
            if (userData.UserId != document.NextActor)
            {
                Snackbar!.Clear();
                Snackbar!.Add("You don't have access to the document", Severity.Info);
                return;
            }
            attachmentList = childRef?.GetAttchments() ?? new List<DocAttachment>();

            if (editMode && userData.UserId == document.NextActor)
            {
                await ProcessDocumentSigning();
            }
            else
            {
                await ProcessDocumentUpload();
            }

        }
        catch (Exception ex)
        {
            logger!.LogDebug(ex.Message);
        }
    }

    private async Task ProcessDocumentSigning()
    {
        var nextActor = document?.NextActor;
        var documentEdited = childRef?.GetDocumentEditStatus() ?? false;

        if (docWorkflowList.Count >= 0)
        {
            var currentIndex = docWorkflowList.FindIndex(dd => dd.ActorId == int.Parse(document.NextActor));
            nextActor = currentIndex >= 0 && currentIndex < docWorkflowList.Count - 1 ? docWorkflowList[currentIndex + 1].ActorId.ToString() : null;
        }

        var docAttachments = attachmentList.Select(dd => new DocumentAmendment
        {
            Content = dd.Type == AppConstants.AttachmentType.Signature ? "" : dd.Content,
            Height = dd.Height,
            Width = dd.Width,
            PageHeight = dd.PageHeight,
            PageWidth = dd.PageWidth,
            PositionX = dd.PositionX,
            PositionY = dd.PositionY,
            Page = dd.Page - 1,
            Type = dd.Type,
            CreatedBy = int.Parse(userData.UserId),
            OrganisationId = int.Parse(userData.OrgId),
        }).ToArray();
        // string jsonrequest = JsonConvert.SerializeObject(docAttachments);
        var signingRequest = new
        {
            ForwardedTo = nextActor ?? "-1",
            DocumentId = DocId,
            ConfirmDocUpdate = documentEdited,
            Attachments = docAttachments
        };
        var (status, result, message) = await httpSvc!.ExecuteRequestAsync<ApiResponse<string>>(HttpVerb.Post, $"api/documents/sign?Status={approvalStatus.Id}", signingRequest);
        if (status == false)
        {
            Snackbar!.Clear();
            Snackbar!.Add("An error occured while signing the document. " + message, Severity.Error);
            return;
        }

        Snackbar!.Clear();
        Snackbar!.Add($"Document signed successfully. {result?.Data}", Severity.Success);
        await Task.Delay(2000);
        navManager.NavigateTo("/documents");
    }

    private async Task ProcessDocumentUpload()
    {
        var (uploaded, uploadResult) = await UploadDocumentToServer();
        if (uploaded == false || string.IsNullOrEmpty(docRef))
        {
            // show toaster, message = "Document reference has not yet been generated"
            return;
        }
        document.AuthorId = int.Parse(userData.UserId);
        document.DocumentReference = docRef;
        document.Path = uploadResult;
        document.ExtraFields = extraFields.Select(dd => new DocumentExtraField { FieldId = dd.FieldId, FieldValue = dd.SelectedValue }).ToArray();
        document.Amendments = attachmentList.Select(dd => new DocumentAmendment
        {
            Content = dd.Type == AppConstants.AttachmentType.Signature ? "" : dd.Content,
            Height = dd.Height,
            Width = dd.Width,
            PageHeight = dd.PageHeight,
            PageWidth = dd.PageWidth,
            PositionX = dd.PositionX,
            PositionY = dd.PositionY,
            Page = dd.Page - 1,
            Type = dd.Type,

            OrganisationId = int.Parse(organisationId),
        }).ToArray();
        var (status, result, message) = await httpSvc!.ExecuteRequestAsync<ApiResponse<string>>(HttpVerb.Post, $"api/document", document);
        if (status == false || result?.Data == null)
        {
            //show dialog at this point
            return;
        }

    }

    private async Task<(bool, string)> UploadDocumentToServer()
    {
        try
        {
            var attachment = attachmentList.FirstOrDefault(at => at.Type == AppConstants.AttachmentType.Document);
            if (document == null)
                return (false, "Document not found");

            var fileBytes = Convert.FromBase64String(attachment?.Content);
            if (fileBytes == null || fileBytes.Length <= 0)
            {
                // show toaster, message = "Document reference has not yet been generated"
                logger!.LogInformation("Couldn't proceed with the upload");
                return (false, "Couldn't proceed with the upload");
            }

            logger!.LogInformation("Proceeding to send to the server");
            var _docRef = Guid.NewGuid().ToString();
            var (status, result, message) = await httpSvc!.UploadDocumentAsync<DocUploadResult>($"api/upload", fileBytes, userData.UserId, _docRef, fileName, $"store_{document.TypeId}");
            if (status == false || result?.Name == null)
            {
                logger!.LogInformation(message);
                return (false, message!);
            }

            docRef = _docRef;
            return (true, result?.Name!);
        }
        catch (Exception ex)
        {
            logger!.LogDebug(ex.Message);
            return (false, ex.Message);
        }

    }

    // This controls the type-to-search filtering
    private async Task<IEnumerable<ListOption>> SearchCountries(string value, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length < 3)
            return usersList.AsEnumerable();

        var (_, result, _) = await httpSvc!.GetAsync<ApiResponse<List<ListOption>>>($"api/listoptions/{OrgId}?type=usersearch&category={value}");

        var userList = result?.Data ?? new List<ListOption>();

        return userList;
    }

    private void ItemUpdated(MudItemDropInfo<KeyValue> dropItem)
    {
        dropItem.Item.Value = dropItem.DropzoneIdentifier;
    }

    private void MoveUp(ListOption item)
    {
        var index = workflowList.IndexOf(item);
        if (index <= 0) return;

        (workflowList[index - 1], workflowList[index]) =
            (workflowList[index], workflowList[index - 1]);
    }

    private void MoveDown(ListOption item)
    {
        var index = workflowList.IndexOf(item);
        if (index < 0 || index >= workflowList.Count - 1) return;

        (workflowList[index + 1], workflowList[index]) =
            (workflowList[index], workflowList[index + 1]);
    }
    // public void Dispose()
    // {
    //     attachmentList.Clear();
    //     attachmentList = null!;
    //     if (File.Exists(fileName))
    //     {
    //         File.Delete(fileName);
    //     }
    // }

}
