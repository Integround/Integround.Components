# Integround.Components.Log.LogEntries
**Log.*** libraries contain components for logging messages to various destinations. They are released under the MIT license and are also available as Nuget packages.

**Log.LogEntries** component is used to log messages to your LogEntries account. You only need to set the access token of your log set and you are ready to log!

**Integround Components** is a collection of open source integration components to help you build custom integration solutions easily. These components can be used in any .NET application to make it easier to execute many integration-related tasks without writing all code by yourself.

## Usage
To create a single Log.LogEntries logger instance:
```csharp
_log = new LogentriesLogger(token);
_log.LogInfo("This is an info message.");
_log.LogWarning("This is a warning message.");
_log.LogError("This is an error message.");
```
To use in conjunction with **AggregateLogger**. AggregateLogger implements the *ILogger* interface and writes the messages to all added log destinations:
```csharp
var log = new AggregateLogger();
log.Add(new TraceLogger());
log.Add(new LogentriesLogger(token));
log.LogError("This is an error message with an exception object.", new Exception("Test exception"));
_log = log;
```
