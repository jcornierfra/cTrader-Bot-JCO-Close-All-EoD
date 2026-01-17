// -------------------------------------------------------------------------------------------------
//
//    JCO Close All EoD - End of Day Position Closer for cTrader
//
//    This cBot automatically closes all open positions and cancels all pending orders
//    at a specified time each day. It handles Daylight Saving Time (DST) automatically
//    and sends Telegram alerts before and after execution.
//
//    Features:
//    - Automatic closing of all positions at configured time
//    - Automatic cancellation of all pending orders
//    - DST (Daylight Saving Time) automatic management
//    - Telegram alerts (preventive + result summary)
//    - Multi-symbol support (closes all trades on the account)
//    - Detailed logging with P&L total
//
//    Usage: Run on M5 timeframe or lower for accurate time detection
//
//    Author: J. Cornier
//    Version: 1.0
//    Last Updated: 2026-01-17
//
//    GitHub: https://github.com/jcornierfra/cTrader-Bot-JCO-Close-All-EoD
//
// -------------------------------------------------------------------------------------------------

using System;
using System.Linq;
using cAlgo.API;
using cAlgo.API.Internals;

namespace cAlgo.Robots
{
    [Robot(TimeZone = TimeZones.UTC, AccessRights = AccessRights.None)]
    public class JCOCloseAllEoD : Robot
    {
        // Paramètres généraux
        [Parameter("Fuseau Horaire", DefaultValue = "Eastern Standard Time", Group = "General",
                  Description = "Fuseaux horaires courants :\n" +
                               "• New York: \"Eastern Standard Time\" (UTC-5/-4)\n" +
                               "• Londres: \"GMT Standard Time\" (UTC+0/+1)\n" +
                               "• Paris: \"Romance Standard Time\" (UTC+1/+2)\n" +
                               "• Tokyo: \"Tokyo Standard Time\" (UTC+9, pas de DST)\n" +
                               "• Sydney: \"AUS Eastern Standard Time\" (UTC+10/+11)\n" +
                               "• Frankfurt: \"W. Europe Standard Time\" (UTC+1/+2)")]
        public string TimeZoneId { get; set; }

        [Parameter("Heure de fermeture - Heure", DefaultValue = 16, MinValue = 0, MaxValue = 23, Group = "Closing Time")]
        public int CloseHour { get; set; }

        [Parameter("Heure de fermeture - Minutes", DefaultValue = 50, MinValue = 0, MaxValue = 59, Group = "Closing Time")]
        public int CloseMinutes { get; set; }

        [Parameter("Activer messages détaillés", DefaultValue = true, Group = "General")]
        public bool VerboseLogging { get; set; }

        // Paramètres Telegram
        [Parameter("Activer Telegram", DefaultValue = true, Group = "Telegram Alert")]
        public bool EnableTelegram { get; set; }

        [Parameter("Bot Token", Group = "Telegram Alert", DefaultValue = "")]
        public string BotToken { get; set; }

        [Parameter("Chat ID", Group = "Telegram Alert", DefaultValue = "")]
        public string ChatID { get; set; }

        [Parameter("Alerte avant fermeture (minutes)", DefaultValue = 10, MinValue = 1, MaxValue = 60, Group = "Telegram Alert")]
        public int AlertBeforeClosingMinutes { get; set; }

        // Variables privées
        private TimeZoneInfo targetTimeZone;
        private DateTime lastClosingDate = DateTime.MinValue;
        private bool closingExecutedToday = false;
        private bool preAlertSentToday = false;

        protected override void OnStart()
        {
            // Initialiser le fuseau horaire
            InitializeTimeZone();

            Print("═══════════════════════════════════════════════");
            Print($"JCO Close All EoD démarré avec succès");
            Print($"Fuseau horaire: {targetTimeZone.DisplayName}");
            Print($"Heure de fermeture: {CloseHour:D2}:{CloseMinutes:D2}");
            Print($"Heure actuelle ({targetTimeZone.Id}): {GetCurrentTimeInTargetTimezone()}");

            if (EnableTelegram && !string.IsNullOrWhiteSpace(BotToken) && !string.IsNullOrWhiteSpace(ChatID))
            {
                Print($"📱 Telegram: ACTIVÉ");
                Print($"   Alerte préventive: {AlertBeforeClosingMinutes} min avant fermeture");
            }
            else if (EnableTelegram)
            {
                Print($"⚠️ Telegram: ACTIVÉ mais Bot Token ou Chat ID manquant");
            }
            else
            {
                Print($"📱 Telegram: DÉSACTIVÉ");
            }

            Print("═══════════════════════════════════════════════");
        }

        private void InitializeTimeZone()
        {
            try
            {
                targetTimeZone = TimeZoneInfo.FindSystemTimeZoneById(TimeZoneId);
                if (VerboseLogging)
                {
                    Print($"Fuseau horaire configuré: {targetTimeZone.DisplayName}");
                    Print($"Ajustement DST actif: {targetTimeZone.SupportsDaylightSavingTime}");
                }
            }
            catch (Exception ex)
            {
                Print($"❌ Erreur: Fuseau horaire '{TimeZoneId}' non trouvé. Utilisation d'Eastern Standard Time par défaut.");
                Print($"Détails: {ex.Message}");
                targetTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
            }
        }

        protected override void OnBar()
        {
            DateTime currentTime = GetCurrentTimeInTargetTimezone();

            // Vérifier si on est dans un nouveau jour
            if (currentTime.Date != lastClosingDate)
            {
                closingExecutedToday = false;
                preAlertSentToday = false;
                lastClosingDate = currentTime.Date;

                if (VerboseLogging)
                {
                    Print($"─────────────────────────────────────────────");
                    Print($"📅 Nouveau jour détecté: {currentTime:yyyy-MM-dd}");
                    Print($"   Heure locale: {currentTime:HH:mm:ss}");
                    Print($"   Heure serveur UTC: {Server.Time:HH:mm:ss}");
                    Print($"   DST actif: {targetTimeZone.IsDaylightSavingTime(currentTime)}");
                    Print($"─────────────────────────────────────────────");
                }
            }

            // Vérifier si c'est le moment d'envoyer l'alerte préventive
            if (!preAlertSentToday && IsPreAlertTime(currentTime))
            {
                SendPreClosingAlert(currentTime);
            }

            // Vérifier si c'est l'heure de fermeture
            if (!closingExecutedToday && IsClosingTime(currentTime))
            {
                ExecuteClosing(currentTime);
            }
        }

        private DateTime GetCurrentTimeInTargetTimezone()
        {
            try
            {
                return TimeZoneInfo.ConvertTimeFromUtc(Server.Time, targetTimeZone);
            }
            catch (Exception ex)
            {
                Print($"❌ Erreur lors de la conversion de l'heure: {ex.Message}");
                return Server.Time;
            }
        }

        private bool IsPreAlertTime(DateTime currentTime)
        {
            // Calculer l'heure de l'alerte préventive (X minutes avant la fermeture)
            TimeSpan targetTime = new(CloseHour, CloseMinutes, 0);
            TimeSpan alertTime = targetTime.Subtract(TimeSpan.FromMinutes(AlertBeforeClosingMinutes));
            TimeSpan currentTimeOfDay = currentTime.TimeOfDay;

            // Vérifier si on est dans la fenêtre de 2 minutes pour l'alerte
            TimeSpan timeDifference = currentTimeOfDay - alertTime;

            return timeDifference.TotalSeconds >= 0 && timeDifference.TotalMinutes < 2;
        }

        private bool IsClosingTime(DateTime currentTime)
        {
            // Créer la fenêtre de temps pour la fermeture (heure exacte ± 1 minute)
            TimeSpan targetTime = new(CloseHour, CloseMinutes, 0);
            TimeSpan currentTimeOfDay = currentTime.TimeOfDay;

            // Vérifier si on est dans la fenêtre de 2 minutes (la minute cible et la suivante)
            TimeSpan timeDifference = currentTimeOfDay - targetTime;

            return timeDifference.TotalSeconds >= 0 && timeDifference.TotalMinutes < 2;
        }

        private void SendPreClosingAlert(DateTime currentTime)
        {
            preAlertSentToday = true;

            // Compter les positions et ordres
            int positionsCount = Positions.Count;
            int pendingOrdersCount = PendingOrders.Count;

            string message = $"🤖 *JCO Close All EoD - Alerte Opérationnelle*\n\n";
            message += $"✅ Le cBot est opérationnel\n";
            message += $"⏰ Fermeture prévue dans {AlertBeforeClosingMinutes} minutes à {CloseHour:D2}:{CloseMinutes:D2} ET\n\n";
            message += $"📊 État actuel du compte:\n";
            message += $"   • Positions ouvertes: {positionsCount}\n";
            message += $"   • Ordres en attente: {pendingOrdersCount}\n";
            message += $"   • Heure locale: {currentTime:HH:mm:ss}\n";
            message += $"   • DST: {(targetTimeZone.IsDaylightSavingTime(currentTime) ? "Heure d'été" : "Heure standard")}";

            SendTelegramMessage(message);

            Print("═══════════════════════════════════════════════");
            Print($"📢 ALERTE PRÉ-FERMETURE ENVOYÉE");
            Print($"   Positions: {positionsCount} | Ordres: {pendingOrdersCount}");
            Print($"   Fermeture dans {AlertBeforeClosingMinutes} minutes");
            Print("═══════════════════════════════════════════════");
        }

        private void SendTelegramMessage(string message)
        {
            if (!EnableTelegram || IsBacktesting)
            {
                if (VerboseLogging)
                    Print("Telegram désactivé ou backtest en cours - Message non envoyé");
                return;
            }

            if (string.IsNullOrWhiteSpace(BotToken) || string.IsNullOrWhiteSpace(ChatID))
            {
                Print("⚠️ ERREUR: Bot Token ou Chat ID manquant pour Telegram");
                return;
            }

            try
            {
                // Encoder le message pour l'URL et activer le markdown
                var encodedMessage = Uri.EscapeDataString(message);
                var url = $"https://api.telegram.org/bot{BotToken}/sendMessage?chat_id={ChatID}&text={encodedMessage}&parse_mode=Markdown";

                var result = Http.Get(url);

                switch (result.StatusCode)
                {
                    case 0:
                        Print("⚠️ Telegram bot en veille, réveillez-le en lui envoyant un message.");
                        break;

                    case 200:
                        if (VerboseLogging)
                            Print("✅ Message Telegram envoyé avec succès");
                        break;

                    case 400:
                        Print("❌ ERREUR Telegram: Chat ID incorrect");
                        break;

                    case 401:
                        Print("❌ ERREUR Telegram: Bot Token incorrect");
                        break;

                    case 404:
                        Print("❌ ERREUR Telegram: Bot Token ou Chat ID manquant/incorrect");
                        break;

                    default:
                        Print($"❌ ERREUR Telegram inconnue: Code {result.StatusCode}");
                        break;
                }
            }
            catch (Exception ex)
            {
                Print($"❌ Exception lors de l'envoi Telegram: {ex.Message}");
            }
        }

        private void ExecuteClosing(DateTime currentTime)
        {
            closingExecutedToday = true;

            Print("═══════════════════════════════════════════════");
            Print($"⏰ FERMETURE AUTOMATIQUE DÉCLENCHÉE");
            Print($"   Heure: {currentTime:yyyy-MM-dd HH:mm:ss} ({targetTimeZone.Id})");
            Print($"   DST: {(targetTimeZone.IsDaylightSavingTime(currentTime) ? "Heure d'été" : "Heure standard")}");
            Print("═══════════════════════════════════════════════");

            int positionsClosed = 0;
            int ordersCancelled = 0;
            double totalPnL = 0;

            // Fermer toutes les positions
            var allPositions = Positions.ToArray();
            if (allPositions.Length > 0)
            {
                Print($"\n📊 Fermeture de {allPositions.Length} position(s)...");

                foreach (var position in allPositions)
                {
                    double pnl = position.NetProfit;
                    var result = ClosePosition(position);

                    if (result.IsSuccessful)
                    {
                        positionsClosed++;
                        totalPnL += pnl;

                        if (VerboseLogging)
                        {
                            Print($"   ✓ {position.SymbolName} | {position.TradeType} | " +
                                  $"Volume: {position.VolumeInUnits} | P&L: {pnl:F2} {Account.Asset.Name}");
                        }
                    }
                    else
                    {
                        Print($"   ❌ Erreur fermeture position {position.Id} ({position.SymbolName}): {result.Error}");
                    }
                }
            }

            // Annuler tous les ordres en attente
            var allPendingOrders = PendingOrders.ToArray();
            if (allPendingOrders.Length > 0)
            {
                Print($"\n📋 Annulation de {allPendingOrders.Length} ordre(s) en attente...");

                foreach (var order in allPendingOrders)
                {
                    var result = CancelPendingOrder(order);

                    if (result.IsSuccessful)
                    {
                        ordersCancelled++;

                        if (VerboseLogging)
                        {
                            Print($"   ✓ {order.SymbolName} | {order.TradeType} | " +
                                  $"Volume: {order.VolumeInUnits} | Prix: {order.TargetPrice}");
                        }
                    }
                    else
                    {
                        Print($"   ❌ Erreur annulation ordre {order.Id} ({order.SymbolName}): {result.Error}");
                    }
                }
            }

            // Résumé
            Print("\n═══════════════════════════════════════════════");
            Print($"✅ FERMETURE TERMINÉE");
            Print($"   Positions fermées: {positionsClosed}");
            Print($"   Ordres annulés: {ordersCancelled}");

            if (positionsClosed > 0)
            {
                string pnlSign = totalPnL >= 0 ? "+" : "";
                Print($"   P&L Total: {pnlSign}{totalPnL:F2} {Account.Asset.Name}");
            }

            Print($"   Prochaine fermeture: {currentTime.Date.AddDays(1):yyyy-MM-dd} {CloseHour:D2}:{CloseMinutes:D2}");
            Print("═══════════════════════════════════════════════\n");

            // Envoyer l'alerte Telegram avec le résultat
            SendClosingResultAlert(currentTime, positionsClosed, ordersCancelled, totalPnL);
        }

        private void SendClosingResultAlert(DateTime closingTime, int positionsClosed, int ordersCancelled, double totalPnL)
        {
            string message = $"🔒 *JCO Close All EoD - Résultat d'Exécution*\n\n";
            message += $"⏰ Fermeture effectuée à {closingTime:HH:mm:ss} ET\n";
            message += $"📅 Date: {closingTime:yyyy-MM-dd}\n\n";

            // Résumé des actions
            if (positionsClosed == 0 && ordersCancelled == 0)
            {
                message += $"✅ *Aucune action requise*\n";
                message += $"   • Aucune position ouverte\n";
                message += $"   • Aucun ordre en attente\n";
            }
            else
            {
                message += $"📊 *Actions effectuées:*\n";

                if (positionsClosed > 0)
                {
                    string pnlSign = totalPnL >= 0 ? "+" : "";
                    string pnlEmoji = totalPnL >= 0 ? "💰" : "📉";
                    message += $"   {pnlEmoji} {positionsClosed} position(s) fermée(s)\n";
                    message += $"   • P&L Total: {pnlSign}{totalPnL:F2} {Account.Asset.Name}\n";
                }
                else
                {
                    message += $"   • Aucune position à fermer\n";
                }

                if (ordersCancelled > 0)
                {
                    message += $"   🚫 {ordersCancelled} ordre(s) annulé(s)\n";
                }
                else
                {
                    message += $"   • Aucun ordre à annuler\n";
                }
            }

            message += $"\n⏭️ Prochaine fermeture:\n";
            message += $"   {closingTime.Date.AddDays(1):yyyy-MM-dd} à {CloseHour:D2}:{CloseMinutes:D2} ET";

            SendTelegramMessage(message);
        }

        protected override void OnStop()
        {
            Print("─────────────────────────────────────────────");
            Print("JCO Close All EoD arrêté");
            Print("─────────────────────────────────────────────");
        }
    }
}
