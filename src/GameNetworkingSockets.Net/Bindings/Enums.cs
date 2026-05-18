namespace Valve.Sockets;

/// <summary>
/// Flags for <c>SendMessageToConnection</c>. Mirrors the
/// <c>k_nSteamNetworkingSend_*</c> constants from
/// <c>steamnetworkingtypes.h</c>.
/// </summary>
[Flags]
public enum SendFlags
{
    Unreliable = 0,
    NoNagle = 1 << 0,
    UnreliableNoNagle = NoNagle,
    NoDelay = 1 << 2,
    UnreliableNoDelay = NoDelay | NoNagle,
    Reliable = 1 << 3,
    ReliableNoNagle = Reliable | NoNagle,
    UseCurrentThread = 1 << 4,
    AutoRestartBrokenSession = 1 << 5,
}

/// <summary>
/// Discriminator for the <see cref="NetworkingIdentity"/> tagged union.
/// Mirrors <c>ESteamNetworkingIdentityType</c>.
/// </summary>
public enum IdentityType
{
    Invalid = 0,
    SteamID = 16,
    XboxPairwiseID = 17,
    SonyPSN = 18,
    GoogleStadia = 19,
    IPAddress = 1,
    GenericString = 2,
    GenericBytes = 3,
    UnknownType = 4,
}

/// <summary>
/// Connection lifecycle state. Mirrors
/// <c>ESteamNetworkingConnectionState</c>.
/// </summary>
public enum ConnectionState
{
    None = 0,
    Connecting = 1,
    FindingRoute = 2,
    Connected = 3,
    ClosedByPeer = 4,
    ProblemDetectedLocally = 5,

    // States that aren't normally observable by API consumers (only via the
    // raw internal connection list); included for completeness.
    FinWait = -1,
    Linger = -2,
    Dead = -3,
}

/// <summary>Scope tag for <c>SetConfigValue</c>. Mirrors <c>ESteamNetworkingConfigScope</c>.</summary>
public enum ConfigurationScope
{
    Global = 1,
    SocketsInterface = 2,
    ListenSocket = 3,
    Connection = 4,
}

/// <summary>Discriminator for the configuration value payload union. Mirrors <c>ESteamNetworkingConfigDataType</c>.</summary>
public enum ConfigurationDataType
{
    Int32 = 1,
    Int64 = 2,
    Float = 3,
    String = 4,
    FunctionPtr = 5,
}

/// <summary>
/// Configuration option keys understood by <see cref="NetworkingUtils.SetConfigurationValue"/>.
/// Mirrors <c>ESteamNetworkingConfigValue</c>; the upstream list is large and not
/// fully enumerated here. Values are stable.
/// </summary>
public enum ConfigurationValue
{
    Invalid = 0,
    FakePacketLossSend = 2,
    FakePacketLossRecv = 3,
    FakePacketLagSend = 4,
    FakePacketLagRecv = 5,
    FakePacketReorderSend = 6,
    FakePacketReorderRecv = 7,
    FakePacketReorderTime = 8,
    FakePacketDupSend = 26,
    FakePacketDupRecv = 27,
    FakePacketDupTimeMax = 28,
    TimeoutInitial = 24,
    TimeoutConnected = 25,
    SendBufferSize = 9,
    SendRateMin = 10,
    SendRateMax = 11,
    NagleTime = 12,
    IPAllowWithoutAuth = 23,
    MTUPacketSize = 32,
    MTUDataSize = 33,
    Unencrypted = 34,
    EnumerateDevVars = 35,
    SymmetricConnect = 37,
    LocalVirtualPort = 38,
    ConnectionStatusChanged = 201,
    AuthStatusChanged = 202,
    RelayNetworkStatusChanged = 203,
    MessagesSessionRequest = 204,
    MessagesSessionFailed = 205,
    P2PSTUNServerList = 103,
    P2PTransportICEEnable = 104,
    P2PTransportICEPenalty = 105,
    P2PTransportSDRPenalty = 106,
    SDRClientConsecutitivePingTimeoutsFailInitial = 19,
    SDRClientConsecutitivePingTimeoutsFail = 20,
    SDRClientMinPingsBeforePingAccurate = 21,
    SDRClientSingleSocket = 22,
    SDRClientForceRelayCluster = 29,
    SDRClientDebugTicketAddress = 30,
    SDRClientForceProxyAddr = 31,
    SDRClientFakeClusterPing = 36,
    LogLevelAckRTT = 13,
    LogLevelPacketDecode = 14,
    LogLevelMessage = 15,
    LogLevelPacketGaps = 16,
    LogLevelP2PRendezvous = 17,
    LogLevelSDRRelayPings = 18,
}

/// <summary>Result of a <c>GetConfigValue</c> call. Mirrors <c>ESteamNetworkingGetConfigValueResult</c>.</summary>
public enum ConfigurationValueResult
{
    BadValue = -1,
    BadScopeObject = -2,
    BufferTooSmall = -3,
    OK = 1,
    OKInherited = 2,
}

/// <summary>Severity for the debug output callback. Mirrors <c>ESteamNetworkingSocketsDebugOutputType</c>.</summary>
public enum DebugType
{
    None = 0,
    Bug = 1,
    Error = 2,
    Important = 3,
    Warning = 4,
    Message = 5,
    Verbose = 6,
    Debug = 7,
    Everything = 8,
}

/// <summary>
/// Result/error codes returned by GameNetworkingSockets APIs. Mirrors
/// <c>EResult</c> from <c>steamclientpublic.h</c>; only a small subset is
/// produced in the open-source standalone build.
/// </summary>
public enum Result
{
    OK = 1,
    Fail = 2,
    NoConnection = 3,
    InvalidPassword = 5,
    LoggedInElsewhere = 6,
    InvalidProtocolVer = 7,
    InvalidParam = 8,
    FileNotFound = 9,
    Busy = 10,
    InvalidState = 11,
    InvalidName = 12,
    InvalidEmail = 13,
    DuplicateName = 14,
    AccessDenied = 15,
    Timeout = 16,
    Banned = 17,
    AccountNotFound = 18,
    InvalidSteamID = 19,
    ServiceUnavailable = 20,
    NotLoggedOn = 21,
    Pending = 22,
    EncryptionFailure = 23,
    InsufficientPrivilege = 24,
    LimitExceeded = 25,
    Revoked = 26,
    Expired = 27,
    AlreadyRedeemed = 28,
    DuplicateRequest = 29,
    AlreadyOwned = 30,
    IPNotFound = 31,
    PersistFailed = 32,
    LockingFailed = 33,
    LogonSessionReplaced = 34,
    ConnectFailed = 35,
    HandshakeFailed = 36,
    IOFailure = 37,
    RemoteDisconnect = 38,
    ShoppingCartNotFound = 39,
    Blocked = 40,
    Ignored = 41,
    NoMatch = 42,
    AccountDisabled = 43,
    ServiceReadOnly = 44,
    AccountNotFeatured = 45,
    AdministratorOK = 46,
    ContentVersion = 47,
    TryAnotherCM = 48,
    PasswordRequiredToKickSession = 49,
    AlreadyLoggedInElsewhere = 50,
    Suspended = 51,
    Cancelled = 52,
    DataCorruption = 53,
    DiskFull = 54,
    RemoteCallFailed = 55,
    PasswordUnset = 56,
    ExternalAccountUnlinked = 57,
    PSNTicketInvalid = 58,
    ExternalAccountAlreadyLinked = 59,
    RemoteFileConflict = 60,
    IllegalPassword = 61,
    SameAsPreviousValue = 62,
    AccountLogonDenied = 63,
    CannotUseOldPassword = 64,
    InvalidLoginAuthCode = 65,
    AccountLogonDeniedNoMail = 66,
    HardwareNotCapableOfIPT = 67,
    IPTInitError = 68,
    ParentalControlRestricted = 69,
    FacebookQueryError = 70,
    ExpiredLoginAuthCode = 71,
    IPLoginRestrictionFailed = 72,
    AccountLockedDown = 73,
    AccountLogonDeniedVerifiedEmailRequired = 74,
    NoMatchingURL = 75,
    BadResponse = 76,
    RequirePasswordReEntry = 77,
    ValueOutOfRange = 78,
    UnexpectedError = 79,
    Disabled = 80,
    InvalidCEGSubmission = 81,
    RestrictedDevice = 82,
    RegionLocked = 83,
    RateLimitExceeded = 84,
    AccountLoginDeniedNeedTwoFactor = 85,
    ItemDeleted = 86,
    AccountLoginDeniedThrottle = 87,
    TwoFactorCodeMismatch = 88,
    TwoFactorActivationCodeMismatch = 89,
    AccountAssociatedToMultiplePartners = 90,
    NotModified = 91,
    NoMobileDevice = 92,
    TimeNotSynced = 93,
    SmsCodeFailed = 94,
    AccountLimitExceeded = 95,
    AccountActivityLimitExceeded = 96,
    PhoneActivityLimitExceeded = 97,
    RefundToWallet = 98,
    EmailSendFailure = 99,
    NotSettled = 100,
    NeedCaptcha = 101,
    GSLTDenied = 102,
    GSOwnerDenied = 103,
    InvalidItemType = 104,
    IPBanned = 105,
    GSLTExpired = 106,
    InsufficientFunds = 107,
    TooManyPending = 108,
    NoSiteLicensesFound = 109,
    WGNetworkSendExceeded = 110,
}

/// <summary>
/// Availability tri-state used by the relay/auth status APIs. Mirrors
/// <c>ESteamNetworkingAvailability</c>.
/// </summary>
public enum Availability
{
    CannotTry = -102,
    Failed = -101,
    Previously = -100,
    Retrying = -10,
    NeverTried = 1,
    Waiting = 2,
    Attempting = 3,
    Current = 100,
    Unknown = 0,
}
