using UnityEngine;
using System.IO;
using System.Runtime.CompilerServices;

/// <summary>
/// Utility class for custom formatted console logging.
/// </summary>
public static class Logger
{
    private const string ScriptColor = "#80D8FF"; // Sky Blue

    #region Extension Methods

    /// <summary>
    /// Extension method to call Note directly from any Unity Object (e.g., this.Note("msg")).
    /// </summary>
    [HideInCallstack]
    public static void Note(this Object context, object message, [CallerFilePath] string filePath = "")
    {
        string scriptName = Path.GetFileNameWithoutExtension(filePath);
        Debug.Log(FormatMessage(message, scriptName, context), context);
    }
    /// <summary>
    /// Notes an informational message with the calling script's name and optional context object.
    /// </summary>
    /// <param name="message">The content to log.</param>
    /// <param name="context">The object context (usually 'this') to extract the name and enable console pinging.</param>
    /// <param name="filePath">Automatically populated by the compiler.</param>
    [HideInCallstack]
    public static void Note(object message, Object context = null, [CallerFilePath] string filePath = "")
    {
        string scriptName = Path.GetFileNameWithoutExtension(filePath);
        Debug.Log(FormatMessage(message, scriptName, context), context);
    }


    [HideInCallstack]
    public static void Warn(this Object context, object message, [CallerFilePath] string filePath = "")
    {
        string scriptName = Path.GetFileNameWithoutExtension(filePath);
        Debug.LogWarning(FormatMessage(message, scriptName, context), context);
    }
    /// <summary>
    /// Warns about a potential issue with the calling script's name and optional context object.
    /// </summary>
    [HideInCallstack]
    public static void Warn(object message, Object context = null, [CallerFilePath] string filePath = "")
    {
        string scriptName = Path.GetFileNameWithoutExtension(filePath);
        Debug.LogWarning(FormatMessage(message, scriptName, context), context);
    }


    [HideInCallstack]
    public static void Error(this Object context, object message, [CallerFilePath] string filePath = "")
    {
        string scriptName = Path.GetFileNameWithoutExtension(filePath);
        Debug.LogError(FormatMessage(message, scriptName, context), context);
    }

    /// <summary>
    /// Alerts to an error with the calling script's name and optional context object.
    /// </summary>
    [HideInCallstack]
    public static void Error(object message, Object context = null, [CallerFilePath] string filePath = "")
    {
        string scriptName = Path.GetFileNameWithoutExtension(filePath);
        Debug.LogError(FormatMessage(message, scriptName, context), context);
    }

    #endregion

    #region Formatting

    [HideInCallstack]
    private static string FormatMessage(object message, string scriptName, Object context)
    {
        string objectNamePart = context != null ? $" in {context.name}" : string.Empty;
        return $"<color={ScriptColor}>[{scriptName}{objectNamePart}]</color> {message}";
    }

    #endregion
}