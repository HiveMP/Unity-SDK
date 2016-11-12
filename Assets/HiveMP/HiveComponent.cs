using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using HiveMP.ErrorReporting.Api;
using HiveMP.ErrorReporting.Model;
using HiveMP.TemporarySession.Api;
using HiveMP.TemporarySession.Model;
using Newtonsoft.Json;
using UnityEngine;
using Object = UnityEngine.Object;

public class HiveComponent : MonoBehaviour
{
    // Public Authentication
    //
    // User Authentication
    //
    //

    [Header("Public Authentication")]
    public string PublicAPIKey;
    public PublicAuthenticationModeEnum TemporarySession = PublicAuthenticationModeEnum.AutomaticallyCreateSession;

    [Header("User Authentication"), ReadOnly]
    public UserAuthenticationModeEnum UserSession = UserAuthenticationModeEnum.DisallowUserSessionCreation;

    [Header("Error Reporting")]
    public ErrorReportingModeEnum ErrorReportingMode = ErrorReportingModeEnum.ReportOnlyRuntimeErrors;

    [Header("NAT Punchthrough"), ReadOnly]
    public NATPunchthroughModeEnum NATPunchthroughMode = NATPunchthroughModeEnum.DoNotUseNATPunchthrough;

    [ReadOnly]
    public int NATPunchthroughExposedPort = 0;

    public enum NATPunchthroughModeEnum
    {
        MaintainExposedUDPPort,
        DoNotUseNATPunchthrough
    }

    public enum PublicAuthenticationModeEnum
    {
        AutomaticallyCreateSession,
        CreateSessionOnRequest,
        DisallowTemporarySessionCreation
    }

    public enum UserAuthenticationModeEnum
    {
        CreateSessionOnRequestAndReplaceTemporarySession,
        CreateSessionOnRequestAndMaintainTemporarySession,
        DisallowUserSessionCreation
    }

    public enum ErrorReportingModeEnum
    {
        AlwaysReport,
        ReportOnlyRuntimeErrors,
        NeverReport
    }

    private delegate void Action();

    private bool _tempSessionProcessing;
    private bool _shouldHaveTemporarySession;
    private TemporarySessionApi _tempSessionClient;
    private TempSessionWithSecrets _tempSessionWithSecrets;
    private Queue<Action> _queuedOperations;
    private object _queueLock;
    private Queue<Exception> _queuedExceptions;

    private ErrorReportingApi _errorReportingClient;

    public static bool ValidateServerCertificate(
        object sender,
        System.Security.Cryptography.X509Certificates.X509Certificate certificate,
        X509Chain chain,
        System.Net.Security.SslPolicyErrors sslPolicyErrors)
    {
        bool isOk = true;
        // If there are errors in the certificate chain, look at each error to determine the cause.
        if (sslPolicyErrors != SslPolicyErrors.None)
        {
            for (int i = 0; i < chain.ChainStatus.Length; i++)
            {
                if (chain.ChainStatus[i].Status != X509ChainStatusFlags.RevocationStatusUnknown)
                {
                    chain.ChainPolicy.RevocationFlag = X509RevocationFlag.EntireChain;
                    chain.ChainPolicy.RevocationMode = X509RevocationMode.Online;
                    chain.ChainPolicy.UrlRetrievalTimeout = new TimeSpan(0, 1, 0);
                    chain.ChainPolicy.VerificationFlags = X509VerificationFlags.AllFlags;
                    bool chainIsValid = chain.Build((X509Certificate2)certificate);
                    if (!chainIsValid)
                    {
                        isOk = false;
                    }
                }
            }
        }
        return isOk;
    }

    public void Start()
    {
        Object.DontDestroyOnLoad(this);

        Debug.Log("HiveMP started.");

        ServicePointManager.ServerCertificateValidationCallback = new System.Net.Security.RemoteCertificateValidationCallback(ValidateServerCertificate);
        ServicePointManager.Expect100Continue = true;

        _queuedOperations = new Queue<Action>();
        _queuedExceptions = new Queue<Exception>();
        _queueLock = new object();

        _tempSessionClient = new TemporarySessionApi();
        _errorReportingClient = new ErrorReportingApi();

        HiveMP.ErrorReporting.Client.Configuration.ApiKey["api_key"] = PublicAPIKey;

        Application.logMessageReceivedThreaded += ApplicationOnLogMessageReceivedThreaded;
        AppDomain.CurrentDomain.UnhandledException += CurrentDomainOnUnhandledException;

        var thread = new Thread(new ThreadStart(Run));
        thread.IsBackground = true;
        thread.Start();
    }

    private void CurrentDomainOnUnhandledException(object sender, UnhandledExceptionEventArgs unhandledExceptionEventArgs)
    {
        Debug.LogException((Exception)unhandledExceptionEventArgs.ExceptionObject);
    }

    public void Update()
    {
        while (_queuedExceptions.Count > 0)
        {
            Debug.LogException(_queuedExceptions.Dequeue());
        }

        UpdateTemporarySession();
        //UpdateUserSession();
        UpdateErrorReporting();
        //UpdateNATPunchthrough();
    }

    private void UpdateErrorReporting()
    {
        if (_tempSessionWithSecrets != null)
        {
            HiveMP.TemporarySession.Client.Configuration.ApiKey["api_key"] = _tempSessionWithSecrets.ApiKey;
        }
        else
        {
            HiveMP.TemporarySession.Client.Configuration.ApiKey["api_key"] = PublicAPIKey;
        }
    }

    public TempSessionWithSecrets GetTempSessionWithSecrets()
    {
        return _tempSessionWithSecrets;
    }

    private int GetCurrentUNIXTimestamp()
    {
        System.DateTime epochStart = new System.DateTime(1970, 1, 1, 0, 0, 0, System.DateTimeKind.Utc);
        return (int) (System.DateTime.UtcNow - epochStart).TotalSeconds;
    }

    private void ApplicationOnLogMessageReceivedThreaded(string logString, string stackTrace, LogType type)
    {
        if (type == LogType.Error || type == LogType.Exception)
        {
            var error = new HiveMP.ErrorReporting.Model.Error
            {
                ApplicationName = "Unity",
                ApplicationVersion = "0.0.1",
                EnvironmentData = new EnvironmentInformation
                {
                    CpuArchitectureNameIsPresent = false,
                    CpuArchitectureName = string.Empty,
                    GpuDeviceNameIsPresent = false,
                    GpuDeviceName = string.Empty,
                    OperatingSystemNameIsPresent = false,
                    OperatingSystemName = string.Empty,
                    OperatingSystemVersionIsPresent = false,
                    OperatingSystemVersion = string.Empty
                },
                Message = logString + " " + stackTrace,
                StackTrace = new List<StackTraceEntry>(),
                UniquenessHash = string.Empty,
                Userdata = new Dictionary<string, object>()
            };

            lock (_queueLock)
            {
                _queuedOperations.Enqueue(() =>
                {
                    _errorReportingClient.ErrorPOST(JsonConvert.SerializeObject(error));
                    Debug.Log("Reported error to HiveMP");
                });
            }
        }
    }

    private void UpdateTemporarySession()
    {
        if (_tempSessionProcessing)
        {
            return;
        }

        if (TemporarySession == PublicAuthenticationModeEnum.AutomaticallyCreateSession)
        {
            _shouldHaveTemporarySession = true;
        }
        else if (TemporarySession == PublicAuthenticationModeEnum.DisallowTemporarySessionCreation)
        {
            _shouldHaveTemporarySession = false;
        }

        if (_tempSessionWithSecrets == null)
        {
            Debug.Log("Creating temporary session");
            _tempSessionProcessing = true;
            lock (_queueLock)
            {
                _queuedOperations.Enqueue(() =>
                {
                    try
                    {
                        HiveMP.TemporarySession.Client.Configuration.ApiKey["api_key"] = PublicAPIKey;
                        _tempSessionWithSecrets = _tempSessionClient.SessionPUT();
                    }
                    finally
                    {
                        _tempSessionProcessing = false;
                    }
                });
            }
        }
        else if (!_shouldHaveTemporarySession)
        {
            // Delete the session since we have one.
            Debug.Log("Session is present, but should not be; deleting");
            _tempSessionProcessing = true;
            lock (_queueLock)
            {
                _queuedOperations.Enqueue(() =>
                {
                    try
                    {
                        HiveMP.TemporarySession.Client.Configuration.ApiKey["api_key"] = _tempSessionWithSecrets.ApiKey;
                        _tempSessionClient.SessionDELETE(_tempSessionWithSecrets.Id);
                        _tempSessionProcessing = false;
                    }
                    finally
                    {
                        _tempSessionWithSecrets = null;
                    }
                });
            }
        }
        else
        {
            // Check if we need to renew the session.
            if ((int)_tempSessionWithSecrets.Expiry.Value - GetCurrentUNIXTimestamp() < 15*60)
            {
                Debug.Log("Less than 15 minutes to session expiry; automatically renewing");
                _tempSessionProcessing = true;
                lock (_queueLock)
                {
                    _queuedOperations.Enqueue(() =>
                    {
                        try
                        {
                            HiveMP.TemporarySession.Client.Configuration.ApiKey["api_key"] = _tempSessionWithSecrets.ApiKey;
                            var result = _tempSessionClient.SessionPOST(_tempSessionWithSecrets.Id);
                            _tempSessionWithSecrets.Expiry = result.Expiry;
                        }
                        finally
                        {
                            _tempSessionProcessing = false;
                        }
                    });
                }
            }
        }
    }

    private void Run()
    {
        while (true)
        {
            if (_queuedOperations.Count == 0)
            {
                Thread.Sleep(1000);
            }
            else
            {
                while (_queuedOperations.Count > 0)
                {
                    Action op;
                    lock (_queueLock)
                    {
                        op = _queuedOperations.Dequeue();
                    }
                    try
                    {
                        op();
                    }
                    catch (Exception ex)
                    {
                        _queuedExceptions.Enqueue(ex);
                    }
                }
            }
        }
    }
    
}
