using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace Diamond.Model
{
    // Mirrors WEB/server/src/NaverSabermetrics.Web/DiamondModels.cs (camelCase JSON).
    // Positions use the web game's axes: home plate z=0, mound z<0, metres.
    [Serializable] public class Vec2 { public double x; public double y; }
    [Serializable] public class Position3 { public double x; public double y; public double z; }
    [Serializable] public class BodyHit { public double at; public Position3 position; }
    [Serializable] public class Swing { public double at; public Vec2 aim; }
    [Serializable] public class Contact { public double at; public Position3 position; }

    [Serializable]
    public class Pitch
    {
        public int id;
        public string type = "fastball";
        public double velocity;
        public double releaseAt;
        public double flightMs;
        public double? releaseX;
        public double? releaseY;
        public double? releaseZ;
        public Vec2 target = new Vec2();
        public double breakX;
        public double breakY;
        public double quality;
        public bool resolved;
        public PitchResult reaction;
        public Swing aiBatterSwing;
        public BodyHit bodyHit;
    }

    [Serializable]
    public class PitchResult
    {
        public int id;
        public string label = "";
        public string kind = "ball";      // strike | ball | foul | hit | out | walk | hbp
        public string outcome = "BALL";   // BALL STRIKE MISS FOUL OUT 1B 2B 3B HR K BB HBP
        public double? timing;
        public double? aimError;
        public double quality;
        public double distance;
        public double exitSpeed;          // km/h
        public double launchAngle;        // degrees
        public double direction;          // radians, 0 = straight to centre field
        public int points;
        public bool plateEnded;
        public double at;
        public double? swingAt;
        public Vec2 swingAim;
        public Vec2 plateLocation;
        public BodyHit bodyHit;
        public Contact contact;
        public string trajectory;         // ground | line | fly | foul
    }

    [Serializable]
    public class ActionView
    {
        public string format;
        public string code;
        public string mode;
        public string role;
        public string batter;
        public string pitcher;
        public string pace;
        public int round;
        public int balls;
        public int strikes;
        public int score;
        public int pitchCount;
        public Pitch pitch;
        public List<PitchResult> history = new List<PitchResult>();
        public bool waiting;
        public bool done;
        public string winner;
        public long serverNow;
        public long expiresAt;
        public long serverReceivedAt;
        public long serverSentAt;
    }

    public static class DiamondJson
    {
        public static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
        {
            NullValueHandling = NullValueHandling.Ignore,
            MissingMemberHandling = MissingMemberHandling.Ignore,
        };
        public static T Parse<T>(string json) => JsonConvert.DeserializeObject<T>(json, Settings);
    }
}
