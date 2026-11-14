using System.Net;

namespace CsRaiders.Api.Application.Common.EmailTemplates.Layouts;

/// <summary>
/// Provides a shared responsive HTML wrapper layout and sanitization utilities for all transactional emails.
/// </summary>
public static class HtmlEmailLayout
{
    public const string BrandName = "CS Raiders";

    /// <summary>
    /// Escapes HTML-sensitive characters to prevent markup and script injection.
    /// </summary>
    public static string Sanitize(string? input) => WebUtility.HtmlEncode(input ?? string.Empty);

    /// <summary>
    /// Wraps inner HTML content in a responsive, branded email envelope with header, container, and footer.
    /// </summary>
    /// <param name="pageTitle">The document title for web preview.</param>
    /// <param name="contentHtml">The sanitized inner HTML content.</param>
    /// <returns>A complete, standalone HTML document string.</returns>
    public static string Wrap(string pageTitle, string contentHtml)
    {
        var encodedTitle = Sanitize(pageTitle);

        return $@"<!DOCTYPE html>
<html lang=""en"">
<head>
    <meta charset=""UTF-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
    <meta http-equiv=""X-UA-Compatible"" content=""IE=edge"">
    <title>{encodedTitle}</title>
    <style>
        body {{
            margin: 0;
            padding: 0;
            font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Helvetica, Arial, sans-serif;
            background-color: #f4f5f7;
            color: #172b4d;
            -webkit-text-size-adjust: 100%;
        }}
        table {{
            border-collapse: collapse;
        }}
        .email-wrapper {{
            width: 100%;
            background-color: #f4f5f7;
            padding: 32px 0;
        }}
        .email-container {{
            max-width: 600px;
            margin: 0 auto;
            background-color: #ffffff;
            border-radius: 8px;
            overflow: hidden;
            box-shadow: 0 1px 3px rgba(0, 0, 0, 0.1);
            border: 1px solid #e2e8f0;
        }}
        .email-header {{
            background-color: #0f172a;
            padding: 24px 32px;
            text-align: center;
        }}
        .brand-logo {{
            color: #ffffff;
            font-size: 24px;
            font-weight: 700;
            letter-spacing: 0.5px;
            margin: 0;
        }}
        .email-body {{
            padding: 32px;
            line-height: 1.6;
            font-size: 16px;
            color: #1e293b;
        }}
        .email-footer {{
            padding: 24px 32px;
            text-align: center;
            font-size: 13px;
            color: #64748b;
            background-color: #f8fafc;
            border-top: 1px solid #e2e8f0;
        }}
    </style>
</head>
<body style=""margin: 0; padding: 0; background-color: #f4f5f7; -webkit-text-size-adjust: 100%;"">
    <div class=""email-wrapper"" style=""background-color: #f4f5f7; padding: 32px 0;"">
        <table role=""presentation"" width=""100%"" border=""0"" cellspacing=""0"" cellpadding=""0"">
            <tr>
                <td align=""center"">
                    <div class=""email-container"" style=""max-width: 600px; margin: 0 auto; background-color: #ffffff; border-radius: 8px; overflow: hidden; border: 1px solid #e2e8f0;"">
                        <div class=""email-header"" style=""background-color: #0f172a; padding: 24px 32px; text-align: center;"">
                            <h1 class=""brand-logo"" style=""color: #ffffff; font-size: 24px; font-weight: 700; margin: 0;"">{BrandName}</h1>
                        </div>
                        <div class=""email-body"" style=""padding: 32px; line-height: 1.6; font-size: 16px; color: #1e293b;"">
                            {contentHtml}
                        </div>
                        <div class=""email-footer"" style=""padding: 24px 32px; text-align: center; font-size: 13px; color: #64748b; background-color: #f8fafc; border-top: 1px solid #e2e8f0;"">
                            <p style=""margin: 0 0 8px;"">&copy; {BrandName}. All rights reserved.</p>
                            <p style=""margin: 0;"">This is an automated notification, please do not reply directly to this email.</p>
                        </div>
                    </div>
                </td>
            </tr>
        </table>
    </div>
</body>
</html>";
    }
}
