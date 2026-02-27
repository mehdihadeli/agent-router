namespace Common.ErrorHandling.Exceptions;

public class ConflictException(string message, System.Exception? innerException = null)
    : CustomException(message, StatusCodes.Status409Conflict, innerException);
