namespace AgentRouter.ErrorHandling.Exceptions;

public class ValidationException(
    string message,
    System.Exception? innerException = null,
    params string[] errors
) : BadRequestException(message, innerException, errors);
