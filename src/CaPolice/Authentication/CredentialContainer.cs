using Azure.Core;
using System;

namespace CaPolice.Authentication;

internal class CredentialContainer
{
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
