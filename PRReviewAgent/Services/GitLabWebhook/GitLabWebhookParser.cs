using System.Text.Json;
using System.Text.Json.Serialization;

namespace PRReviewAgent.Services.GitLabWebhook
{
    public static class GitLabWebhookParser
    {
        public static GitLabMrNoteWebhook ParseAndValidateNoteWebhook(string jsonPayload)
        {
            // 1. Perform basic JSON deserialization with options to allow some type mismatches
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                NumberHandling = JsonNumberHandling.AllowReadingFromString
            };

            GitLabMrNoteWebhook? payload;
            try
            {
                payload = JsonSerializer.Deserialize<GitLabMrNoteWebhook>(jsonPayload, options);
            }
            catch (JsonException ex)
            {
                throw new FormatException("Invalid JSON syntax.", ex);
            }

            // 2. Check for null payload and verify required fields in nested structures
            if (payload == null)
                throw new ArgumentNullException(nameof(jsonPayload), "Payload is empty.");

            // Ensure this is a comment event for a Merge Request
            if (payload.ObjectKind != "note" || payload.ObjectAttributes?.NoteableType != "MergeRequest")
                throw new InvalidOperationException("This is not a Merge Request comment event.");

            // 3. Validate specific required fields strictly
            if (payload.Project?.Id == null)
                throw new KeyNotFoundException("Required field 'project.id' is missing or null.");

            if (payload.ObjectAttributes?.Id == null)
                throw new KeyNotFoundException("Required field 'object_attributes.id' is missing or null.");

            if (string.IsNullOrEmpty(payload.ObjectAttributes?.Note))
                throw new KeyNotFoundException("Required field 'object_attributes.note' is missing, null, or empty.");

            if (payload.MergeRequest == null)
                throw new KeyNotFoundException("Required object 'merge_request' was not found.");

            var mr = payload.MergeRequest;

            if (mr.Iid == null)
                throw new KeyNotFoundException("Required field 'merge_request.iid' is missing or null.");

            if (mr.TargetProjectId == null)
                throw new KeyNotFoundException("Required field 'merge_request.target_project_id' is missing or null.");

            if (string.IsNullOrEmpty(mr.SourceBranch))
                throw new KeyNotFoundException("Required field 'merge_request.source_branch' is missing, null, or empty.");

            if (string.IsNullOrEmpty(mr.Title))
                throw new KeyNotFoundException("Required field 'merge_request.title' is missing, null, or empty.");

            // Description can be an empty string, so only check for null
            if (mr.Description == null)
                throw new KeyNotFoundException("Required field 'merge_request.description' is null.");

            return payload;
        }

        public static GitLabMergeRequestWebhook ParseAndValidateMergeRequest(string jsonPayload)
        {
            // 1. Configure JSON serializer options (allow loose type matches and case insensitivity)
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                NumberHandling = JsonNumberHandling.AllowReadingFromString
            };

            GitLabMergeRequestWebhook? payload;
            try
            {
                payload = JsonSerializer.Deserialize<GitLabMergeRequestWebhook>(jsonPayload, options);
            }
            catch (JsonException ex)
            {
                throw new FormatException("The JSON payload syntax is invalid.", ex);
            }

            // 2. Validate root object and event type
            if (payload == null)
                throw new ArgumentNullException(nameof(jsonPayload), "The payload is empty.");

            if (payload.ObjectKind != "merge_request")
                throw new InvalidOperationException("The event type is not a merge_request.");

            // 3. Strictly validate the presence of the required fields
            if (payload.Project?.Id == null)
                throw new KeyNotFoundException("The required field 'project.id' is missing or null.");

            if (payload.ObjectAttributes == null)
                throw new KeyNotFoundException("The required object 'object_attributes' is missing.");

            var attrs = payload.ObjectAttributes;

            if (attrs.Iid == null)
                throw new KeyNotFoundException("The required field 'object_attributes.iid' is missing or null.");

            if (attrs.SourceProjectId == null)
                throw new KeyNotFoundException("The required field 'object_attributes.source_project_id' is missing or null.");

            if (string.IsNullOrEmpty(attrs.SourceBranch))
                throw new KeyNotFoundException("The required field 'object_attributes.source_branch' is missing, null, or empty.");

            // Validate the newly added 'action' field
            if (string.IsNullOrEmpty(attrs.Action))
                throw new KeyNotFoundException("The required field 'object_attributes.action' is missing, null, or empty.");

            return payload;
        }

    }
}
