using System;
using Microsoft.AspNetCore.Components;
using MudBlazor;
using Newtonsoft.Json;
using zxadocsfe.Dtos;
using zxadocsfe.Helpers;
using zxadocsfe.Services;
using zxadocslib.Dtos;
using zxadocsui.Components.DocWorkflow;
using zxadocsui.State;

namespace zxadocsui.Components.Pages.Dashboard;

public partial class CreateWorkflow
{
    [Inject] ILogger<CreateWorkflow>? Logger { get; set; } = default!;
    [Inject] IHttpService? HttpSvc { get; set; } = default!;
    [Inject] IDialogService? DialogService { get; set; }
    [Inject] IUserSession? Session { get; set; }
    [Inject] ISnackbar? Snackbar { get; set; }
    [Inject] NavigationManager? Navigator { get; set; }
    private DocumentsWorkflow? wkflowRef;
    private DocumentEditor? childRef;
    private DocumentDetails? docRef;
    private List<DocsWorkflow> workflowList = new();
    private List<DocAttachment> attachmentList = new();
    private List<DocCategoryField> extraFields = new();
    Document document = new();
    UserData userData = new();
    bool validForm;
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            userData = await Session!.GetCurrentUser();
        }
    }
    private async Task OnPreviewInteraction(StepperInteractionEventArgs arg)
    {
        if (arg.Action == StepAction.Complete)
        {
            // occurrs when clicking next
            await ControlStepCompletion(arg);
        }
        else if (arg.Action == StepAction.Activate)
        {
            // occurrs when clicking a step header with the mouse
            await ControlStepNavigation(arg);
        }
    }

    private async Task ControlStepCompletion(StepperInteractionEventArgs arg)
    {
        switch (arg.StepIndex)
        {
            case 0:
                var validated = docRef!.ValidateForm();
                validForm = !validated;
                if (!validated)
                {
                    await DialogService!.ShowMessageBoxAsync("Error", "Please provide all required document details");
                    arg.Cancel = true;
                }
                break;
            case 1:
                workflowList = wkflowRef!.WorflowList();
                if (workflowList?.Count <= 0)
                {
                    await DialogService!.ShowMessageBoxAsync("Error", "Please provide the documents worflow");
                    arg.Cancel = true;
                }
                break;
            case 2:
                attachmentList = childRef!.GetAttchments();
                if (attachmentList.Count <= 1)
                {
                    await DialogService!.ShowMessageBoxAsync("Error", "You need to attach at least one document");
                    arg.Cancel = true;
                }
                var uploaded = await ProcessDocumentUpload();
                if (!uploaded)
                    arg.Cancel = true;
                break;
        }
    }

    private async Task ProcessDocumentSigning()
    {
        var docAttachments = attachmentList.Select(dd => new DocumentAmendment
        {
            Content = dd.Type == AppConstants.AttachmentType.Signature ? "" : dd.Content,
            Height = dd.Height,
            Width = dd.Width,
            PositionX = dd.PositionX,
            PositionY = dd.PositionY,
            Page = dd.Page - 1,
            Type = dd.Type,
            CreatedBy = int.Parse(userData.UserId),
            OrganisationId = int.Parse(userData.OrgId),
        }).ToArray();
        string jsonrequest = JsonConvert.SerializeObject(docAttachments);
        var (status, result, message) = await HttpSvc!.ExecuteRequestAsync<ApiResponse<string>>(HttpVerb.Post, $"api/documents/sign/{document.Id}/{document.NextActor}", docAttachments);
        if (status == false)
        {
            Snackbar!.Clear();
            Snackbar!.Add("An error occured while signing the document. " + message, Severity.Error);
            return;
        }

        Snackbar!.Clear();
        Snackbar!.Add($"Document signed successfully. {result?.Data}", Severity.Success);
        await Task.Delay(2000);
        Navigator!.NavigateTo("/new-documents");
    }

    private async Task<bool> ProcessDocumentUpload()
    {
        string docRef = Guid.NewGuid().ToString();
        var (uploaded, uploadResult) = await UploadDocumentToServer(docRef);
        if (uploaded == false)
        {
            // show toaster, message = "Document reference has not yet been generated"
            Snackbar!.Add(uploadResult, Severity.Error);
            return false;
        }
        document.AuthorId = int.Parse(userData.UserId);
        document.NextActor = workflowList[0].ActorId + "";
        document.DocumentReference = docRef;
        document.Path = uploadResult;
        document.Workflows = workflowList.Select(fl => new DocumentWorkflow { ActorId = fl.ActorId, IsFinal = fl.IsFinal, Level = fl.Level, WorkflowType = fl.WorkflowType }).ToArray();
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
            OrganisationId = int.Parse(userData.OrgId),
        }).ToArray();
        var data = JsonConvert.SerializeObject(document);
        var (status, result, message) = await HttpSvc!.ExecuteRequestAsync<ApiResponse<string>>(HttpVerb.Post, $"api/document", document);
        if (status == false || result?.Data == null)
        {
            //show dialog at this point
            Snackbar!.Add(message!, Severity.Error);
            return false;
        }
        return true;
    }

    private async Task<(bool, string)> UploadDocumentToServer(string _docRef)
    {
        try
        {
            string fileName = $"{userData.UserId}_{Guid.NewGuid().ToString().Replace("-", "")}.pdf";
            var attachmentDoc = attachmentList.FirstOrDefault(dd => dd.Type == AppConstants.AttachmentType.Document);
            if (attachmentDoc == null)
                return (false, "Couldn't proceed with the upload");

            var fileBytes = Convert.FromBase64String(attachmentDoc.Content);
            Logger!.LogDebug("Proceeding to send to the server");
            var (status, result, message) = await HttpSvc!.UploadDocumentAsync<DocUploadResult>($"api/upload", fileBytes, userData.UserId, _docRef, fileName, $"store_{document.TypeId}");
            if (status == false || result?.Name == null)
            {
                Snackbar!.Clear();
                Snackbar!.Add(message ?? "Unknown error on uploading document", Severity.Error);
                return (false, message!);
            }
            attachmentList.Remove(attachmentDoc);
            return (true, result?.Name!);
        }
        catch (Exception ex)
        {
            Logger!.LogDebug(ex.Message);
            return (false, ex.Message);
        }

    }

    private async Task ControlStepNavigation(StepperInteractionEventArgs arg)
    {
        switch (arg.StepIndex)
        {
            case 1:
                workflowList = wkflowRef!.WorflowList();
                if (workflowList.Count <= 0)
                {
                    await DialogService!.ShowMessageBoxAsync("Error", "Please provide the documents worflow");
                    arg.Cancel = true;
                }
                break;
            case 2:
                attachmentList = childRef!.GetAttchments();
                if (attachmentList.Count <= 1)
                {
                    await DialogService!.ShowMessageBoxAsync("Error", "Finish step 1 and 2 first");
                    arg.Cancel = true;
                }
                break;
        }
    }



}
