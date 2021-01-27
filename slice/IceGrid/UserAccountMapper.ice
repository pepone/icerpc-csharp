//
// Copyright (c) ZeroC, Inc. All rights reserved.
//

#pragma once

[[suppress-warning(reserved-identifier)]]

[cs:namespace(ZeroC)]
module IceGrid
{
    /// This exception is raised if a user account for a given session
    /// identifier can't be found.
    exception UserAccountNotFoundException
    {
    }

    /// A user account mapper object is used by IceGrid nodes to map
    /// session identifiers to user accounts.
    interface UserAccountMapper
    {
        /// Get the name of the user account for the given user. This is
        /// used by IceGrid nodes to figure out the user account to use
        /// to run servers.
        ///
        /// @param user The value of the server descriptor's <code>user</code>
        /// attribute. If this attribute is not defined, and the server's
        /// activation mode is <code>session</code>, the default value of
        /// <code>user</code> is the session identifier.
        ///
        /// @return The user account name.
        ///
        /// @throws UserAccountNotFoundException Raised if no user account
        /// is found for the given user.
        string getUserAccount(string user);
    }
}
