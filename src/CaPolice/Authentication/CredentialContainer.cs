using Azure.Core;
using System;

namespace CaPolice.Authentication;

internal class CredentialContainer
{
    private static readonly CredentialContainer _instance = new();

    internal static CredentialContainer Instance => _instance;

    private CredentialContainer() { }

    private TokenCredential? _tokenCredential;

    internal TokenCredential? TokenCredential
    {
        get => _tokenCredential;
        set
        {
            ArgumentNullException.ThrowIfNull(value, nameof(TokenCredential));
            _tokenCredential = value;
        }
    }

    internal void Clear()
    {
        _tokenCredential = null;
    }


}
