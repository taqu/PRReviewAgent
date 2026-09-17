namespace PRReviewAgent.Services.AutoReview
{
    public sealed class AutoReviewPolicy : IAutoReviewPolicy
    {
        private readonly AutoReviewOptions _options;
        private readonly IAutoReviewUserSettingRepository _repository;

        public AutoReviewPolicy(AutoReviewOptions options, IAutoReviewUserSettingRepository repository)
        {
            _options = options;
            _repository = repository;
        }

        public async Task<bool> IsEnabledAsync(long projectId, string userId, CancellationToken ct)
        {
            if (!_options.Enabled) return false;
            bool? userSetting = await _repository.GetAsync(projectId, userId, ct);
            if (userSetting.HasValue) return userSetting.Value;
            return _options.Mode == AutoReviewMode.OptOut;
        }
    }
}
