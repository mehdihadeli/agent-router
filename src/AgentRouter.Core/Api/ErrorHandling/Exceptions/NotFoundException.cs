namespace AgentRouter.Core.Api.ErrorHandling.Exceptions;

public class NotFoundException(string message, Exception? innerException = null)
    : CustomException(message, StatusCodes.Status404NotFound, innerException);
