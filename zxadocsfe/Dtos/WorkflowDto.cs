using System;

namespace zxadocsfe.Dtos;

public class WorkflowDto
{
    public int RoleId { get; set; }
    public bool IsRequired { get; set; }
    public string Lable { get; set; } = "";
}
