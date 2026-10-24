import { useGetAuthMe } from "../api/generated/auth/auth";

// Who is signed in and what they can see, from /auth/me. isAdmin and branchIds
// come from the server's live lookup, not the token, so they stay current.
export function useCurrentUser() {
  const me = useGetAuthMe();

  return {
    isLoading: me.isLoading,
    isLoaded: me.data !== undefined,
    email: me.data?.email ?? null,
    isAdmin: me.data?.isAdmin ?? false,
    branchIds: (me.data?.branchIds ?? []).map(Number),
  };
}
