using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data.Linq;
using System.Data.Linq.SqlClient;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Transactions;
using System.Web.Services;
using PlasmaShared;
using PlasmaShared.ContentService;
using ZeroKWeb;
using ZeroKWeb.Controllers;
using ZeroKWeb.SpringieInterface;
using ZkData;
using BalanceTeamsResult = ZeroKWeb.SpringieInterface.BalanceTeamsResult;
using BattlePlayerResult = ZeroKWeb.SpringieInterface.BattlePlayerResult;
using BattleResult = ZeroKWeb.SpringieInterface.BattleResult;
using BotTeam = ZeroKWeb.SpringieInterface.BotTeam;
using ProgramType = ZkData.ProgramType;
using RecommendedMapResult = ZeroKWeb.SpringieInterface.RecommendedMapResult;
using ResourceType = ZkData.ResourceType;
using SpringBattleStartSetup = ZeroKWeb.SpringieInterface.SpringBattleStartSetup;

namespace ZeroKWeb
{
    /// <summary>
    /// Summary description for ContentService
    /// </summary>
    [WebService(Namespace = "http://tempuri.org/")]
    [WebServiceBinding(ConformsTo = WsiProfiles.BasicProfile1_1)]
    [ToolboxItem(false)]
    // To allow this Web Service to be called from script, using ASP.NET AJAX, uncomment the following line. 
        // [System.Web.Script.Services.ScriptService]
    public class ContentService: WebService
    {

        [WebMethod]
        public bool DownloadFile(string internalName,
                                 out List<string> links,
                                 out byte[] torrent,
                                 out List<string> dependencies,
                                 out ResourceType resourceType,
                                 out string torrentFileName)
        {
            return PlasmaServer.DownloadFile(internalName, out links, out torrent, out dependencies, out resourceType, out torrentFileName);
        }

        [WebMethod]
        public List<PlasmaServer.ResourceData> FindResourceData(string[] words, ResourceType? type = null)
        {
            var db = new ZkDataContext();
            var ret = db.Resources.AsQueryable();
            if (type == ResourceType.Map) ret = ret.Where(x => x.TypeID == ResourceType.Map);
            if (type == ResourceType.Mod) ret = ret.Where(x => x.TypeID == ResourceType.Mod);
            var test = ret.Where(x => x.InternalName == string.Join(" ", words));
            if (test.Any()) return test.OrderByDescending(x => -x.FeaturedOrder).Select(x => new PlasmaServer.ResourceData(x)).ToList();
            int i;
            if (words.Length == 1 && int.TryParse(words[0], out i)) ret = ret.Where(x => x.ResourceID == i);
            else
            {
                foreach (var w in words)
                {
                    var w1 = w;
                    ret = ret.Where(x => SqlMethods.Like(x.InternalName, "%" + w1 + "%"));
                }
            }
            return ret.OrderByDescending(x => -x.FeaturedOrder).Take(400).Select(x => new PlasmaServer.ResourceData(x)).ToList();
        }

       
        [WebMethod]
        public List<string> GetEloTop10()
        {
            using (var db = new ZkDataContext())
            return
                db.Accounts.Where(x => x.SpringBattlePlayers.Any(y => y.SpringBattle.StartTime > DateTime.UtcNow.AddMonths(-1))).OrderByDescending(
                    x => x.Elo1v1).Select(x => x.Name).Take(10).ToList();
        }


    

        /// <summary>
        /// Finds resource by either md5 or internal name
        /// </summary>
        /// <param name="md5"></param>
        /// <param name="internalName"></param>
        /// <returns></returns>
        [WebMethod]
        public PlasmaServer.ResourceData GetResourceData(string md5, string internalName)
        {
            return PlasmaServer.GetResourceData(md5, internalName);
        }

        [WebMethod]
        public PlasmaServer.ResourceData GetResourceDataByInternalName(string internalName)
        {
            var db = new ZkDataContext();
            var entry = db.Resources.SingleOrDefault(x => x.InternalName == internalName);
            if (entry != null) return new PlasmaServer.ResourceData(entry);
            else return null;
        }

        [WebMethod]
        public PlasmaServer.ResourceData GetResourceDataByResourceID(int resourceID)
        {
            var db = new ZkDataContext();
            return new PlasmaServer.ResourceData(db.Resources.Single(x => x.ResourceID == resourceID));
        }


        [WebMethod]
        public List<PlasmaServer.ResourceData> GetResourceList(DateTime? lastChange, out DateTime currentTime)
        {
            return PlasmaServer.GetResourceList(lastChange, out currentTime);
        }


        [WebMethod]
        public ScriptMissionData GetScriptMissionData(string name)
        {
            using (var db = new ZkDataContext())
            {
                var m = db.Missions.Single(x => x.Name == name && x.IsScriptMission);
                return new ScriptMissionData()
                       {
                           MapName = m.Map,
                           ModTag = m.ModRapidTag,
                           StartScript = m.Script,
                           ManualDependencies = m.ManualDependencies != null ? new List<string>(m.ManualDependencies.Split('\n')) : null,
                           Name = m.Name
                       };
            }
        }


        [WebMethod]
        public void NotifyMissionRun(string login, string missionName)
        {
            missionName = Mission.GetNameWithoutVersion(missionName);
            using (var db = new ZkDataContext()){
                db.Missions.Single(x => x.Name == missionName).MissionRunCount++;
                Account.AccountByName(db,login).MissionRunCount++;
                db.SubmitChanges();
            }
        }


        [WebMethod]
        public PlasmaServer.ReturnValue RegisterResource(int apiVersion,
                                                         string springVersion,
                                                         string md5,
                                                         int length,
                                                         ResourceType resourceType,
                                                         string archiveName,
                                                         string internalName,
                                                         int springHash,
                                                         byte[] serializedData,
                                                         List<string> dependencies,
                                                         byte[] minimap,
                                                         byte[] metalMap,
                                                         byte[] heightMap,
                                                         byte[] torrentData)
        {
            return PlasmaServer.RegisterResource(apiVersion,
                                                 springVersion,
                                                 md5,
                                                 length,
                                                 resourceType,
                                                 archiveName,
                                                 internalName,
                                                 springHash,
                                                 serializedData,
                                                 dependencies,
                                                 minimap,
                                                 metalMap,
                                                 heightMap,
                                                 torrentData);
        }

        [WebMethod]
        public void SubmitMissionScore(string login, string passwordHash, string missionName, int score, int gameSeconds)
        {
            missionName = Mission.GetNameWithoutVersion(missionName);

            using (var db = new ZkDataContext())
            {
                var acc = AuthServiceClient.VerifyAccountHashed(login, passwordHash);
                if (acc == null) throw new ApplicationException("Invalid login or password");

                acc.XP += GlobalConst.XpForMissionOrBots;

                var mission = db.Missions.Single(x => x.Name == missionName);

                if (score != 0)
                {
                    var scoreEntry = mission.MissionScores.FirstOrDefault(x => x.AccountID == acc.AccountID);
                    if (scoreEntry == null)
                    {
                        scoreEntry = new MissionScore() { MissionID = mission.MissionID, AccountID = acc.AccountID, Score = int.MinValue };
                        mission.MissionScores.Add(scoreEntry);
                    }

                    if (score > scoreEntry.Score)
                    {
                        var max = mission.MissionScores.Max(x => (int?)x.Score);
                        if (max == null || max <= score)
                        {
                            mission.TopScoreLine = login;
                            acc.XP += 150; // 150 for getting top score
                        }
                        scoreEntry.Score = score;
                        scoreEntry.Time = DateTime.UtcNow;
                        scoreEntry.MissionRevision = mission.Revision;
                        scoreEntry.GameSeconds = gameSeconds;
                    }
                }

                // ====================
                // campaign stuff
                CampaignPlanet planet = db.CampaignPlanets.FirstOrDefault(p => p.MissionID == mission.MissionID);
                if (planet != null)
                {
                    // first mark this planet as completed - but only if it's already unlocked
                    AccountCampaignProgress progress = db.AccountCampaignProgress.FirstOrDefault(x => x.AccountID == acc.AccountID && x.PlanetID == planet.PlanetID && x.CampaignID == planet.CampaignID);
                    bool alreadyCompleted = false;
                    if (progress != null)
                    {
                        alreadyCompleted = progress.IsCompleted;
                    }
                    else if (planet.StartsUnlocked)
                    {
                        progress = new AccountCampaignProgress() { AccountID = acc.AccountID, CampaignID = planet.CampaignID, PlanetID = planet.PlanetID, IsCompleted = false, IsUnlocked = true };
                        db.AccountCampaignProgress.InsertOnSubmit(progress);
                        db.SubmitChanges();
                    }

                    if (progress != null && planet.IsUnlocked(acc.AccountID))
                    {
                        progress.IsCompleted = true;
                        if (!alreadyCompleted) db.CampaignEvents.InsertOnSubmit(Global.CreateCampaignEvent("Planet completed: {0}", planet));

                        // unlock this planet's journals if appropriate
                        var journals = db.CampaignJournals.Where(x => x.CampaignID == planet.CampaignID && x.PlanetID == planet.PlanetID);
                        foreach (CampaignJournal journal in journals)
                        {
                            bool proceed = true;

                            var requiredVars = db.CampaignJournalVars.Where(x => x.CampaignID == journal.CampaignID && x.JournalID == journal.JournalID).ToList();
                            if (requiredVars.Count() == 0)
                            {
                            	proceed = false; // no need to add to unlock table; it'll be marked as unlocked at runtime
                                if (journal.UnlockOnPlanetCompletion && !alreadyCompleted) 
                                {
                                    db.CampaignEvents.InsertOnSubmit(Global.CreateCampaignEvent("Journal entry unlocked: {0}", journal));
                                }
                            }
                            else
                            {
                                foreach (CampaignJournalVar variable in requiredVars)
                                {
                                    var accountVar = db.AccountCampaignVars.FirstOrDefault(x => x.CampaignID == variable.CampaignID && x.VarID == variable.RequiredVarID);
                                    if (accountVar.Value != variable.RequiredValue)
                                    {
                                        proceed = false;
                                        break;  // failed to meet var requirement, stop here
                                    }
                                }
                            }
                            if (proceed)    // met requirements for unlocking journal
                            {
                                AccountCampaignJournalProgress jp = db.AccountCampaignJournalProgress.FirstOrDefault(x => x.AccountID == acc.AccountID 
                                    && x.CampaignID == journal.CampaignID 
                                    && x.JournalID == journal.JournalID);
                                if (jp == null)
                                {
                                    jp = new AccountCampaignJournalProgress() { AccountID = acc.AccountID, CampaignID = journal.CampaignID, JournalID = journal.JournalID, IsUnlocked = true };
                                    db.AccountCampaignJournalProgress.InsertOnSubmit(jp);
                                    db.SubmitChanges();
                                    db.CampaignEvents.InsertOnSubmit(Global.CreateCampaignEvent("Journal entry unlocked: {0}", journal));
                                }
                                else if (!jp.IsUnlocked)
                                {
                                    jp.IsUnlocked = true;
                                    db.CampaignEvents.InsertOnSubmit(Global.CreateCampaignEvent("Journal entry unlocked: {0}", journal));
                                }
                            }
                        }


                        // unlock planets made available by completing this one
                        var links = db.CampaignLinks.Where(x => x.UnlockingPlanetID == planet.PlanetID && x.CampaignID == planet.CampaignID);
                        foreach (CampaignLink link in links)
                        {
                            CampaignPlanet toUnlock = link.PlanetToUnlock;
                            bool proceed = true;
                            var requiredVars = db.CampaignPlanetVars.Where(x => x.CampaignID == toUnlock.CampaignID && x.PlanetID == toUnlock.PlanetID);
                            if (requiredVars.Count() == 0) proceed = true;
                            else
                            {
                                foreach (CampaignPlanetVar variable in requiredVars)
                                {
                                    var accountVar = db.AccountCampaignVars.FirstOrDefault(x => x.CampaignID == variable.CampaignID && x.VarID == variable.RequiredVarID);
                                    if (accountVar.Value != variable.RequiredValue)
                                    {
                                        proceed = false;
                                        break;  // failed to meet var requirement, stop here
                                    }
                                }
                                proceed = true;
                            }

                            if (proceed)    // met requirements for unlocking planet
                            {    
                                AccountCampaignProgress progress2 = db.AccountCampaignProgress.FirstOrDefault(x => x.AccountID == acc.AccountID 
                                    && x.CampaignID == toUnlock.CampaignID 
                                    && x.PlanetID == toUnlock.PlanetID);
                                bool alreadyUnlocked = false;
                                if (progress2 == null)
                                {
                                    progress2 = new AccountCampaignProgress() { AccountID = acc.AccountID, CampaignID = toUnlock.CampaignID, PlanetID = toUnlock.PlanetID, IsCompleted = false, IsUnlocked = true };
                                    db.AccountCampaignProgress.InsertOnSubmit(progress2);
                                    db.SubmitChanges();
                                    db.CampaignEvents.InsertOnSubmit(Global.CreateCampaignEvent("Planet unlocked: {0}", planet));
                                }
                                else if (!progress2.IsUnlocked)
                                {
                                    progress2.IsUnlocked = true;
                                    db.CampaignEvents.InsertOnSubmit(Global.CreateCampaignEvent("Planet unlocked: {0}", planet));
                                }
                                else alreadyUnlocked = true;

                                // unlock their journals too if appropriate
                                var journals2 = db.CampaignJournals.Where(x => x.CampaignID == planet.CampaignID && x.PlanetID == planet.PlanetID);
                                foreach (CampaignJournal journal in journals)
                                {
                                    bool proceedJ = true;
                                    var requiredVarsJ = db.CampaignJournalVars.Where(x => x.CampaignID == journal.CampaignID && x.JournalID == journal.JournalID).ToList();
                                    if (requiredVarsJ.Count() == 0)
                                    {
                                    	proceedJ = false;   // no need to add to unlock table; it'll be marked as unlocked at runtime
                                        if (journal.UnlockOnPlanetUnlock && !alreadyUnlocked)
                                        {
                                            db.CampaignEvents.InsertOnSubmit(Global.CreateCampaignEvent("Journal entry unlocked: {0}", journal));
                                        }
                                    }
                                    else
                                    {
                                        foreach (CampaignJournalVar variableJ in requiredVarsJ)
                                        {
                                            var accountVar = db.AccountCampaignVars.FirstOrDefault(x => x.CampaignID == variableJ.CampaignID && x.VarID == variableJ.RequiredVarID);
                                            if (accountVar.Value != variableJ.RequiredValue)
                                            {
                                                proceedJ = false;
                                                break;  // failed to meet var requirement, stop here
                                            }
                                        }
                                    }
                                    if (proceedJ)    // met requirements for unlocking journal
                                    {
                                        AccountCampaignJournalProgress jp = db.AccountCampaignJournalProgress.FirstOrDefault(x => x.AccountID == acc.AccountID && x.JournalID == journal.JournalID);
                                        if (jp == null)
                                        {
                                            jp = new AccountCampaignJournalProgress() { AccountID = acc.AccountID, CampaignID = journal.CampaignID, JournalID = journal.JournalID, IsUnlocked = true };
                                            db.AccountCampaignJournalProgress.InsertOnSubmit(jp);
                                            db.SubmitChanges();
                                            db.CampaignEvents.InsertOnSubmit(Global.CreateCampaignEvent("Journal entry unlocked: {0}", journal));
                                        }
                                        else if (!jp.IsUnlocked)
                                        {
                                            jp.IsUnlocked = true;
                                            db.CampaignEvents.InsertOnSubmit(Global.CreateCampaignEvent("Journal entry unlocked: {0}", journal));
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
                db.SubmitChanges();
            }
        }


     

        [WebMethod]
        public void SubmitStackTrace(ProgramType programType, string playerName, string exception, string extraData, string programVersion)
        {
            using (var db = new ZkDataContext())
            {
                var exceptionLog = new ExceptionLog
                                   {
                                       ProgramID = programType,
                                       Time = DateTime.UtcNow,
                                       PlayerName = playerName,
                                       ExtraData = extraData,
                                       Exception = exception,
                                       ExceptionHash = new Hash(exception).ToString(),
                                       ProgramVersion = programVersion,
                                       RemoteIP = GetUserIP()
                                   };
                db.ExceptionLogs.InsertOnSubmit(exceptionLog);
                db.SubmitChanges();
            }
        }


        [WebMethod]
        public bool VerifyAccountData(string login, string password)
        {
            var acc = AuthServiceClient.VerifyAccountPlain(login, password);
            if (acc == null) return false;
            return true;
        }

        public class AccountInfo {
            public string Name;
            public int LobbyID;
            public int ZeroKAccountID;
            public int ZeroKLevel;
            public int ClanID;
            public string ClanName;
            public string Country;
            public string Aliases;
            public int LobbyTimeRank;
            public bool IsLobbyAdmin;
            public bool IsZeroKAdmin;
            public string Avatar;
            public float Elo;
            public double EffectiveElo;
            public float EloWeight;
            public string FactionName;
            public int FactionID;
            public int SpringieLevel;
        }

        [WebMethod]
        public AccountInfo GetAccountInfo(string login, string password) {
            var acc = AuthServiceClient.VerifyAccountPlain(login, password);
            if (acc == null) return null;
            else return new AccountInfo()
            {
                Name = acc.Name,
                LobbyID = acc.LobbyID??0,
                Country = acc.Country,
                Aliases = acc.Aliases,
                ZeroKAccountID = acc.AccountID,
                ZeroKLevel = acc.Level,
                ClanID = acc.ClanID??0,
                ClanName = acc.Clan != null? acc.Clan.ClanName : null,
                LobbyTimeRank    = acc.LobbyTimeRank,
                IsLobbyAdmin = acc.IsLobbyAdministrator,
                IsZeroKAdmin= acc.IsZeroKAdmin,
                Avatar = acc.Avatar,
                Elo =(float)acc.Elo,
                EffectiveElo = acc.EffectiveElo,
                EloWeight = (float)acc.EloWeight,
                FactionID = acc.FactionID??0,
                FactionName = acc.Faction != null? acc.Faction.Name:null,
                SpringieLevel = acc.SpringieLevel
            };
        }


        string GetUserIP()
        {
            var ip = Context.Request.ServerVariables["HTTP_X_FORWARDED_FOR"];
            if (string.IsNullOrEmpty(ip) || ip.Equals("unknown", StringComparison.OrdinalIgnoreCase)) ip = Context.Request.ServerVariables["REMOTE_ADDR"];
            return ip;
        }


     

        public class EloInfo
        {
            public double Elo = 1500;
            public double Weight = 1;
        }


        public class ScriptMissionData
        {
            public List<string> ManualDependencies;
            public string MapName;
            public string ModTag;
            public string Name;
            public string StartScript;
        }

    }



}