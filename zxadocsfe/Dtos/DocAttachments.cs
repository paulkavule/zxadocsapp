using System;
using zxadocslib.Dtos;

namespace zxadocsfe.Dtos;

public record DocAttachment : DocumentAmendment
{
    public string ElementId { get; set; } = string.Empty;
}

public record DocUploadResult
{
    public string Name { get; set; } = string.Empty;
}