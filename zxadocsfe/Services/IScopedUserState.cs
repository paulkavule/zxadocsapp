namespace zxadocsfe.Services;

// =====================================================================================
// Per-circuit state that belongs to ONE signed-in user.
//
// Blazor Server scoped services live for the whole SignalR circuit, not for a login. Signing
// out and signing back in as someone else navigates within the same circuit, so every scoped
// instance survives and keeps serving the previous user's data until the browser is refreshed
// (a refresh builds a new circuit, which is why the symptom "disappears" on F5).
//
// Anything caching user-specific state must implement this and be registered as an
// IScopedUserState pointing at the SAME instance, e.g.
//
//     builder.Services.AddScoped<IScopedUserState>(sp => sp.GetRequiredService<AppState>());
//
// UserSession clears all of them on sign-out AND on sign-in — sign-in too, because a session
// can expire straight to the login page without SignOut ever running.
// =====================================================================================
public interface IScopedUserState
{
    void ClearUserState();
}
