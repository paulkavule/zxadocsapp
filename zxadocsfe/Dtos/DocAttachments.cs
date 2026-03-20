using System;
using zxadocslib.Dtos;

namespace zxadocsfe.Dtos;

public record DocAttachment : DocumentAmendment
{
    public string ElementId { get; set; } = string.Empty;
    public bool StopDragClick { get; set; }
}

public record DocsWorkflow : DocumentWorkflow
{
    public string Name { get; set; } = "";
}

public record DocUploadResult
{
    public string Name { get; set; } = string.Empty;
}

public record DocCategoryField : CategoryField
{
    public string SelectedValue { get; set; } = string.Empty;
}