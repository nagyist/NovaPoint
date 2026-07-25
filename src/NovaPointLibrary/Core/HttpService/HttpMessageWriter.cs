using NovaPointLibrary.Commands.Authentication;
using NovaPointLibrary.Core.Authentication;
using NovaPointLibrary.Core.Logging;
using System.Net.Http.Headers;

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

            if (method == HttpMethod.Post || method == HttpMethod.Put || method.Method == "PATCH")
            {
                message.Content = new StringContent(content, System.Text.Encoding.UTF8);
                message.Content.Headers.ContentType = MediaTypeHeaderValue.Parse("application/json");
            }

            LogMessage(message);

            return message;
        }

        private void LogMessage(HttpRequestMessage request)
        {
            //logger.Info(GetType().Name, $"=== HttpRequestMessage ===");
            logger.Info(GetType().Name, $"Method: {request.Method}, Request URI: {request.RequestUri}");

            //logger.Debug(GetType().Name, $"Headers:");
            //foreach (var header in request.Headers)
            //{
            //    logger.Debug(GetType().Name, $"{header.Key}: {header.Value}");
            //}

            //if (request.Content != null)
            //{
            //    logger.Debug(GetType().Name, $"Content Headers:");
            //    foreach (var header in request.Content.Headers)
            //    {
            //        logger.Debug(GetType().Name, $"{header.Key}: {header.Value}");
            //    }

            //    // Read and log the content body (for non-GET requests)
            //    if (request.Method != HttpMethod.Get)
            //    {
            //        var content = request.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            //        logger.Debug(GetType().Name, $"Body: {content}");
            //    }
            //}

            //logger.Debug(GetType().Name, $"==========================");
        }
    }
}
