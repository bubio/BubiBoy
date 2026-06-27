namespace BubiBoy.RetroAchievements

type RaStatus =
    | Disabled
    | LoggedOut
    | Authenticating
    | Ready
    | LoadingGame
    | Active
    | OfflineSession of reason: string

type RaUser =
    { Username: string
      DisplayName: string
      Score: uint32
      SoftcoreScore: uint32 }

type RaGame =
    { Id: uint32
      Title: string
      Hash: string
      ImageUrl: string }

type RaAchievement =
    { Bucket: byte
      BucketLabel: string
      Id: uint32
      Title: string
      Description: string
      Points: uint32
      MeasuredProgress: string
      MeasuredPercent: float32
      Rarity: float32
      State: byte
      Unlocked: byte
      ImageUrl: string }

type RaLeaderboardEntry =
    { Username: string
      Rank: uint32
      Score: string }

type RaLeaderboard =
    { Bucket: byte
      BucketLabel: string
      Id: uint32
      Title: string
      Description: string
      TrackerValue: string
      State: byte
      Format: byte
      LowerIsBetter: bool
      TopEntries: RaLeaderboardEntry list }

type RaEvent =
    { EventType: uint32
      RelatedId: uint32
      Title: string
      Description: string
      ImageUrl: string
      MeasuredProgress: string
      MeasuredPercent: float32
      Value: string
      BestScore: string
      Rank: uint32
      TotalEntries: uint32
      LeaderboardEntries: RaLeaderboardEntry list
      Generation: int64
      GameId: uint32 }

type RaSnapshot =
    { Status: RaStatus
      HardcoreEnabled: bool
      User: RaUser option
      Game: RaGame option
      Achievements: RaAchievement list
      Leaderboards: RaLeaderboard list
      RichPresence: string option
      Generation: int64 }

type RaPauseDecision =
    | PauseAllowed
    | PauseDenied of framesRemaining: uint32
