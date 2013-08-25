namespace FSharp.Actor

open System
open NLog
open NLog.Config
open NLog.Targets

#if INTERACTIVE
open FSharp.Actor
#endif

module Logger = 

    let Console name =
        let write level (msg, args, exn : exn option) =
            let currentColor = Console.ForegroundColor
            let msg = 
                match exn with
                | Some(err) ->
                    String.Format("{0} {1}-[{2}]: {3} : {4}\n{5}", 
                                       DateTime.UtcNow.ToString("dd/MM/yyyy HH:mm:ss.fff"), 
                                       name,
                                       level,
                                       String.Format(msg, args), err.Message, err.StackTrace)
                | None ->
                     String.Format("{0} {1}-[{2}]: {3}", 
                                       DateTime.UtcNow.ToString("dd/MM/yyyy HH:mm:ss.fff"), 
                                       name,
                                       level,
                                       String.Format(msg, args))
            match level with
            | "info" -> Console.ForegroundColor <- ConsoleColor.Green  
            | "warn" -> Console.ForegroundColor <- ConsoleColor.Yellow
            | "error" -> Console.ForegroundColor <- ConsoleColor.Red
            | _ -> Console.ForegroundColor <- ConsoleColor.White 
            Console.WriteLine(msg)
            Console.ForegroundColor <- currentColor

        { new ILogger with
            member x.Debug(msg,args, exn) = write "debug" (msg,args, exn)
            member x.Info(msg,args, exn) = write "info" (msg, args, exn)
            member x.Warning(msg, args, exn) = write "warn" (msg, args, exn)
            member x.Error(msg, args, exn) = write "error" (msg, args, exn) 
        }

    let NLog (name:string) = 
        let configureNlog() =
            let config = new LoggingConfiguration()
            let layout = Layouts.Layout.op_Implicit @"${date:format=HH\:MM\:ss} ${logger} ${message}"
            let consoleTarget = new ColoredConsoleTarget();
            consoleTarget.Layout <- layout
            config.AddTarget("console", consoleTarget);

#if INTERACTIVE
#else
            let fileTarget = new FileTarget();
            config.AddTarget("file", fileTarget);

            fileTarget.FileName <- Layouts.Layout.op_Implicit ("${basedir}/Logs/" + name + ".log")
            fileTarget.Layout <- layout;

            let rule2 = new LoggingRule("*", LogLevel.Debug, fileTarget);
            config.LoggingRules.Add(rule2);
#endif
            let rule1 = new LoggingRule("*", LogLevel.Debug, consoleTarget);
            config.LoggingRules.Add(rule1);

            LogManager.Configuration <- config;

        if not <| IO.File.Exists("NLog.config")
        then configureNlog()

        let logger = LogManager.GetLogger(name)
        { new ILogger with
            member x.Debug(msg, args, exn) =
                 match exn with
                 | Some(err) -> logger.DebugException(String.Format(msg, args), err)                
                 | _ -> logger.Debug(msg, args)
            member x.Info(msg, args, exn) =
                 match exn with
                 | Some(err) -> logger.InfoException(String.Format(msg, args), err)                
                 | _ -> logger.Info(msg, args)
            member x.Warning(msg, args, exn) =
                 match exn with
                 | Some(err) -> logger.WarnException(String.Format(msg, args), err)                
                 | _ -> logger.Warn(msg, args)
            member x.Error(msg, args, exn) =
                 match exn with
                 | Some(err) -> logger.ErrorException(String.Format(msg, args), err)                
                 | _ -> logger.Error(msg, args)  
        }

    let create name = NLog name
