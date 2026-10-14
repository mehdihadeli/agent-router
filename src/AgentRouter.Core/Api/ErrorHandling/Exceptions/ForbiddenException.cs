namespace AgentRouter.Core.Api.ErrorHandling.Exceptions;

public class ForbiddenException(string message, Exception? innerException = null)
    : CustomException(message, statusCode: StatusCodes.Status403Forbidden, innerException);
