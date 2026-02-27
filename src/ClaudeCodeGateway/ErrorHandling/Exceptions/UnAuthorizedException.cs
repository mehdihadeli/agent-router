namespace Common.ErrorHandling.Exceptions;

public class UnAuthorizedException(string message, Exception? innerException = null)
    : CustomException(message, StatusCodes.Status401Unauthorized, innerException);
