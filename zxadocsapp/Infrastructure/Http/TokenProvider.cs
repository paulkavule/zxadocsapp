using System;
using zxadocsapp.State;
using zxadocsfe.Helpers;

namespace zxadocsapp.Infrastructure.Http;

public interface ITokenProvider
{
    string? Token { get; }
    string? RefreshToken { get; }
    void SetTokens(string token, string refreshToken);
}

public class TokenProvider : ITokenProvider
{
    private readonly AppState _state;

    public TokenProvider(AppState state)
    {
        _state = state;
    }

    public string? Token => _state.Get<string>(AppConstants.StateKey.TOKEN);
    public string? RefreshToken => _state.Get<string>(AppConstants.StateKey.REFRESH_TOKEN);

    public void SetTokens(string token, string refreshToken)
    {
        if (!string.IsNullOrEmpty(token))
            _state.Set(AppConstants.StateKey.TOKEN, token);

        if (!string.IsNullOrEmpty(refreshToken))
            _state.Set(AppConstants.StateKey.REFRESH_TOKEN, refreshToken);
    }
}
