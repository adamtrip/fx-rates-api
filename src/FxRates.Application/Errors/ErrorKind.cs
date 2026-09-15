namespace FxRates.Application.Errors;

/// <summary>The API maps failure kinds to HTTP statuses without parsing messages.</summary>
public enum ErrorKind
{
    Validation,

    NotFound,

    AlreadyExists,

    /// <summary>The winning insert was deleted before the losing lookup could read it. The caller should retry.</summary>
    ConcurrentChange,

    /// <summary>The provider returned an unusable payload.</summary>
    ProviderFailure,

    /// <summary>The provider was unreachable or refused the request, including quota notices.</summary>
    ProviderUnavailable,

    ProviderTimeout
}
