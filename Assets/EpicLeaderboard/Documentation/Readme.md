# EpicLeaderboard Plugin

## Table of Contents
- [Overview](#overview)
- [Features](#features)
- [Requirements](#requirements)
- [Installation](#installation)
- [Setup](#setup)
- [Quickstart](#quickstart)
- [Additional Features](#additional-features)
- [API Reference](#api-reference)
- [Support](#support)
- [License](#license)
- [Changelog](#changelog)

## Overview
**EpicLeaderboard** adds online leaderboards to your Unity game in 5 minutes. 
Built simple enough for game jams, scalable enough for production.

## Features
- Support for all platforms, Desktop, Mobile, VR and Web.
- Support for all Render Pipelines, including Built-in URP and HDRP.
- Ready to use Leaderboard UI Templates, with country flags builtin.
- Server side score formatting for easy online configuration, display
  e.g., "100.323" as "1:40.323" for racing games without touching your code. 
- Timed leaderboards, with support for daily, weekly, monthly and yearly resets.
- Support for country based leaderboard.
- Username profanity filter and game wide uniqueness check.
- Metadata support for entries, to store extra information.
- Double Precision Scores, for games that require more than 9 quadrillion points.

## Requirements
- Unity 2022.3 or later.
- No external dependencies, works out of the box.

## Installation
1. After importing you'll find the package in `Assets/EpicLeaderboard`.
2. Example Scene: `Assets/EpicLeaderboard/Samples/Demo/DemoScene`.

## Setup
After importing, complete these two steps:

**1. Enable Sprite Atlas V2** (required for country flags)  
`Edit → Project Settings → Editor → Sprite Packer → Mode: Sprite Atlas V2 - Enabled`  
Without this, country flags will not render in the leaderboard UI.

**2. Import TextMeshPro Essentials**  
A popup will appear after import. Click **Import TMP Essentials**.

## Quickstart
1. Log into the [EpicLeaderboard Dashboard](https://epicleaderboard.com) and create a new game, and copy the `GameID`, and `GameKey`.
2. Create a new Leaderboard using a `Primary ID`, and optional `Secondary ID`.
   - Primary for the main category, e.g., `Level 1`, `Level 2`, etc.
   - Secondary for difficulty / game mode / etc. `Easy`, `Hard`, etc.

### Create a Game / Leaderboard Definition

#### As Scriptable Objects
Right-click in the project window → Create → EpicLeaderboard → Game  
Right-click in the project window → Create → EpicLeaderboard → Leaderboard

And fill in the data from the website.

Then use the definitions in your MonoBehaviours, e.g.,
```csharp 
[SerializeField] private EpicLeaderboard.EpicLeaderboardGame gameDefinition;
[SerializeField] private EpicLeaderboard.BoardDefinition boardDefinition;
```

#### As Code 
```csharp
// Create Game Definition
var gameDefinition = EpicLeaderboard.EpicLeaderboardGame.Create("gameID", "gameKey");
var boardDefinition = EpicLeaderboard.BoardDefinition.Create("Level 1", "Easy");
```

### Submit a Score
```csharp
EpicLeaderboardClient.SubmitScore(gameDefinition, boardDefinition, username, score, 
    (result) =>
    {
        if (result.Success)
        { 
            if(result.Value.WasNewBest) 
            {
                // Update UI...
            }
        }
        else 
        {
            Debug.LogError($"Submit failed: {result.Error}");
            return;
        }
    });
```

### Fetch Latest Scores
```csharp
EpicLeaderboardClient.GetScores(gameDefinition, boardDefinition, 
    (result) =>
    {
        if (result.Success)
        {
            leaderboardPanel.DisplayResult(result.Value, highlightUsername: "PlayerName");
        }
        else 
        {
            Debug.LogError($"Fetch failed: {result.Error}");
            return;
        }
    }, username: "PlayerName",
    timeframe: Timeframe.AllTime, aroundPlayer: true, localCountryOnly: false);
```

### Display the entries
1. If you don't have a Canvas yet, create one: GameObject → UI → Canvas.
2. Drag the `Assets/EpicLeaderboard/Runtime/UI/Prefabs/LeaderboardPanel` into your Canvas.
3. Get a reference to the `LeaderboardPanel` component. 
4. Call `leaderboardPanel.DisplayResult(...)` with the result of `GetScores`.

## Additional Features

### Check Username Availability
```csharp
EpicLeaderboardClient.IsUsernameAvailable(gameDefinition, username, epicResult =>
    {
        var usernameStatus = epicResult.Value switch
        {
            UsernameAvailability.Available => "Available",
            UsernameAvailability.Taken => "Username taken",
            UsernameAvailability.Invalid => "Invalid characters",
            UsernameAvailability.Profanity => "Not allowed",
            _ => ""
        };
        
        errorLabel.text = usernameStatus;
    });
```

### Metadata Support
You can optionally create a Metadata Map to pass additional information about the score, e.g.,
- Clan name.
- Car model in racing games.

```csharp
Dictionary<string, string> metadata = new Dictionary<string, string>();
metadata["Clan"] = "Red Dragons";
metadata["CarModel"] = "Speedster 3000";

EpicLeaderboardClient.SubmitScore(gameDefinition, boardDefinition, username, score, metadata,
(result) =>
{ 
    // ...
});
```

### Persist Player Username
```csharp
// store in PlayerPrefs
EpicLeaderboard.EpicLeaderboardStorage.Username = "submittedUsername";

// retrieve after game restart 
string lastSubmittedUsername = EpicLeaderboard.EpicLeaderboardStorage.Username;
```

## API Reference
Full documentation: [link](https://epicleaderboard.com/docs/unity/)

## Support
Email: support@epicleaderboard.com

## License
Subject to the Unity Asset Store EULA

## Changelog
See CHANGELOG.md