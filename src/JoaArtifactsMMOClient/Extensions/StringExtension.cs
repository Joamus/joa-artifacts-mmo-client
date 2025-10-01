using System.Text.RegularExpressions;

public static class StringExtension
{
    public static string FromPascalToSnakeCase(this string text)
    {
        text = Regex.Replace(text, "(.)([A-Z][a-z]+)", "$1_$2");
        text = Regex.Replace(text, "([a-z0-9])([A-Z])", "$1_$2");
        return text.ToLower();
    }

    public static string FromSnakeToPascalCase(this string str)
    {
        // Replace all non-letter and non-digits with an underscore and lowercase the rest.
        string sample = string.Join(
            "",
            str.Select(c => char.IsLetterOrDigit(c) ? c.ToString().ToLower() : "_").ToArray()
        );

        // Split the resulting string by underscore
        // Select first character, uppercase it and concatenate with the rest of the string
        var arr = sample
            .Split(['_'], StringSplitOptions.RemoveEmptyEntries)
            .Select(s => $"{s.Substring(0, 1).ToUpper()}{s.Substring(1)}");

        // Join the resulting collection
        sample = string.Join("", arr);

        return sample;
    }
}
