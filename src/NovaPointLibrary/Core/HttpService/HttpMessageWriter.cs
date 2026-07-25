using NovaPointLibrary.Commands.Authentication;
using NovaPointLibrary.Core.Authentication;
using NovaPointLibrary.Core.Logging;
using System.Net.Http.Headers;
using System.Text;

namespace NovaPointLibrary.Core.HttpService
{
    internal class HttpMessageWriter(
        ILogger logger,
        IAppClient appInfo,
        HttpMethod method,
        string uriString,
        string accept = "application/json",
        string content = "",
        Dictionary<string, string>? additionalHeaders = null)
    {
        private static readonly string _className = nameof(HttpMessageWriter);

        private const string _graphHost = "graph.microsoft.com";
        private const string _spoHostSuffix = ".sharepoint.com";

        // Parsed up front so an invalid URL fails at the call site that built it,
        // and so the token audience is chosen from the host instead of a substring match.
        private readonly Uri _uri = Uri.TryCreate(uriString, UriKind.Absolute, out Uri? uri)
            ? uri
            : throw new ArgumentException($"'{uriString}' is not a valid absolute URI.", nameof(uriString));

        internal async Task<HttpRequestMessage> GetMessageAsync()
        {
            if (_uri.Scheme != Uri.UriSchemeHttps)
            {
                throw new InvalidOperationException($"Refusing to send an access token over '{_uri.Scheme}' to '{_uri.Host}'.");
            }

            if (_uri.Host.Equals(_graphHost, StringComparison.OrdinalIgnoreCase))
            {
                return GetMessage(await appInfo.GetGraphAccessToken());
            }
            else if (_uri.Host.EndsWith(_spoHostSuffix, StringComparison.OrdinalIgnoreCase))
            {
                return GetMessage(await appInfo.GetSPOAccessToken(uriString));
            }
            else
            {
                throw new InvalidOperationException($"Unsupported endpoint '{_uri.GetLeftPart(UriPartial.Authority)}'; expected Microsoft Graph or SharePoint Online.");
            }
        }

        private HttpRequestMessage GetMessage(string accessToken)
        {
            HttpRequestMessage message = new(method, _uri);

            message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            message.Headers.Accept.Add(MediaTypeWithQualityHeaderValue.Parse(accept));

            if (additionalHeaders != null)
            {
                foreach (var header in additionalHeaders)
                {
                    message.Headers.Add(header.Key, header.Value);
                }
            }

            if (method == HttpMethod.Post || method == HttpMethod.Put || method == HttpMethod.Patch)
            {
                message.Content = new StringContent(content, Encoding.UTF8, "application/json");
            }

            LogMessage(message);

            return message;
        }

        private void LogMessage(HttpRequestMessage request)
        {
            logger.Info(_className, $"Method: {request.Method}, Request URI: {request.RequestUri}");

            if (request.Content != null)
            {
                logger.Debug(_className, $"Body: {content}");
            }
        }
    }
}
