namespace zxadocsfe.Dtos;

/// <summary>A deployment-wide setting as the API lists it (ZD-316); must match the API's SettingValue.</summary>
public record SettingValue(string Category, string SubCategory, string Name, string Value, bool IsSecret, int OrganisationId);
