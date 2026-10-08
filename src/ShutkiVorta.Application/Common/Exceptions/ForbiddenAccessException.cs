namespace ShutkiVorta.Application.Common.Exceptions;

public sealed class ForbiddenAccessException(string message = "You do not have permission to perform this action.")
    : Exception(message);
