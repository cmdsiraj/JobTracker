// AWS Bedrock via the Converse API. Unlike the other providers this isn't a
// plain HTTP POST with a bearer token — the AWS SDK signs requests with
// SigV4, and credentials can come from two places ("support both", per the
// user):
//   1. An explicit Access Key ID + Secret Access Key saved in Settings
//      (stored like the other providers' keys, via Secrets/DPAPI).
//   2. Failing that, the SDK's default credential provider chain: the
//      AWS_ACCESS_KEY_ID/AWS_SECRET_ACCESS_KEY env vars, a local AWS CLI/SSO
//      profile (%USERPROFILE%\.aws\credentials or \config), or an
//      EC2/ECS/container instance role.

using System.Net;
using Amazon;
using Amazon.BedrockRuntime;
using Amazon.BedrockRuntime.Model;
using Amazon.Runtime;
using Amazon.Runtime.Credentials;

namespace JobTracker.Services;

public static class BedrockClient
{
    public static async Task<string> CompleteAsync(
        string modelId, List<NimClient.Message> messages, int maxTokens = 800, double temperature = 0.1)
    {
        if (string.IsNullOrWhiteSpace(modelId)) throw new NimException(NimErrorKind.Http, "No Bedrock model ID configured");

        await RequestThrottle.Shared.WaitTurnAsync();

        using var client = CreateClient();
        var request = new ConverseRequest
        {
            ModelId = modelId,
            InferenceConfig = new InferenceConfiguration { MaxTokens = maxTokens, Temperature = (float)temperature },
            Messages = [],
        };

        foreach (var message in messages)
        {
            if (message.Role == "system")
            {
                request.System ??= [];
                request.System.Add(new SystemContentBlock { Text = message.Content });
            }
            else
            {
                request.Messages.Add(new Message
                {
                    Role = message.Role == "assistant" ? ConversationRole.Assistant : ConversationRole.User,
                    Content = [new ContentBlock { Text = message.Content }],
                });
            }
        }

        try
        {
            var response = await client.ConverseAsync(request);
            var text = response.Output?.Message?.Content?.FirstOrDefault(c => c.Text is not null)?.Text;
            if (string.IsNullOrEmpty(text)) throw new NimException(NimErrorKind.EmptyResponse);
            return text;
        }
        catch (AmazonServiceException ex) when (ex.StatusCode == HttpStatusCode.TooManyRequests)
        {
            throw new NimException(NimErrorKind.RateLimited);
        }
        catch (AmazonServiceException ex)
        {
            throw new NimException(NimErrorKind.Http, ex.Message);
        }
    }

    /// True when explicit AWS keys are saved in Secrets (path 1 above).
    public static bool HasExplicitCredentials =>
        Secrets.Shared.Get(SecretKey.AwsAccessKeyId) is { Length: > 0 } &&
        Secrets.Shared.Get(SecretKey.AwsSecretAccessKey) is { Length: > 0 };

    /// Best-effort check for whether *some* AWS credential source is
    /// available — explicit keys, or a local profile/env var/instance role
    /// via the default chain — for Settings' status display.
    public static bool HasAnyCredentials()
    {
        if (HasExplicitCredentials) return true;
        try
        {
            new DefaultAWSCredentialsIdentityResolver().ResolveIdentity(new AmazonBedrockRuntimeConfig());
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static AmazonBedrockRuntimeClient CreateClient()
    {
        var region = RegionEndpoint.GetBySystemName(
            string.IsNullOrWhiteSpace(Preferences.Shared.BedrockRegion) ? "us-east-1" : Preferences.Shared.BedrockRegion);

        var accessKey = Secrets.Shared.Get(SecretKey.AwsAccessKeyId);
        var secretKey = Secrets.Shared.Get(SecretKey.AwsSecretAccessKey);
        if (!string.IsNullOrEmpty(accessKey) && !string.IsNullOrEmpty(secretKey))
        {
            return new AmazonBedrockRuntimeClient(new BasicAWSCredentials(accessKey, secretKey), region);
        }
        // No explicit keys: let the SDK resolve from the default chain.
        return new AmazonBedrockRuntimeClient(region);
    }
}
