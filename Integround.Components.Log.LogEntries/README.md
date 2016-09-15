# Integround.Components.Log.LogEntries
**Integround.Components** is a set of libraries containing tools for building custom integration processes. Log libraries contain components for logging messages to various destinations.

**Log.LogEntries** component is used to log the messages to your LogEntries account. You only need to set the access token of your log set and you are ready to log!

## Usage
To create a single Log.LogEntries logger instance:
```csharp
_log = new LogentriesLogger(token);
_log.LogInfo("This is an info message.");
_log.LogWarning("This is a warning message.");
_log.LogError("This is an error message.");
```
To use in conjunction with **AggregateLogger**. AggregateLogger implements the ILogger interface and writes the messages to all added log destinations:
```csharp
_log = new AggregateLogger();
_log.Add(new TraceLogger());
_log.Add(new LogentriesLogger(token));
_log.LogError("This is an error message with an exception object.", new Exception("Test exception"));
```
