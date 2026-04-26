using System.Runtime.InteropServices;

namespace zxadocslib.Helpers;

public class EnvHelper
{
    public static string GetEnvironmentVariable(string variableName)
    {
        var envVar = "";
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            envVar = Environment.GetEnvironmentVariable(variableName, EnvironmentVariableTarget.Process) ?? Environment.GetEnvironmentVariable(variableName.ToUpper(), EnvironmentVariableTarget.Process);
        else
            envVar = Environment.GetEnvironmentVariable(variableName) ?? Environment.GetEnvironmentVariable(variableName.ToUpper());
        return envVar ?? "";

    }

    public static void LoadVariables(string filePath)
    {
        if (!File.Exists(filePath))
        {
            return; // Optionally handle the absence of the file
        }

        var lines = File.ReadAllLines(filePath);
        foreach (var line in lines)
        {
            // Skip comments and empty lines
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#"))
            {
                continue;
            }

            var keyValue = line.Split(new[] { '=' }, 2);
            if (keyValue.Length == 2)
            {
                var key = keyValue[0].Trim();
                var value = keyValue[1].Trim();
                Environment.SetEnvironmentVariable(key, value);
            }
        }
    }

}