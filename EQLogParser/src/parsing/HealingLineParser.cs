﻿using log4net;
using System;
using System.Reflection;

namespace EQLogParser
{
  internal class HealingLineParser
  {
    private static readonly ILog Log = LogManager.GetLogger(MethodBase.GetCurrentMethod()?.DeclaringType);

    private HealingLineParser()
    {

    }

    public static bool Process(LineData lineData)
    {
      var action = lineData.Action;
      try
      {
        int index;
        if (action.Length >= 23 && (index = action.LastIndexOf(" healed ", action.Length, StringComparison.Ordinal)) > -1)
        {
          var record = HandleHealed(action, index, lineData.BeginTime);
          if (record != null)
          {
            record.Healer = PlayerManager.Instance.ReplacePlayer(record.Healer, record.Healed);
            record.Healed = PlayerManager.Instance.ReplacePlayer(record.Healed, record.Healer);
            RecordManager.Instance.Add(record, lineData.BeginTime);
            return true;
          }
        }
      }
      catch (ArgumentNullException ne)
      {
        Log.Error(ne);
      }
      catch (NullReferenceException nr)
      {
        Log.Error(nr);
      }
      catch (ArgumentOutOfRangeException aor)
      {
        Log.Error(aor);
      }
      catch (ArgumentException ae)
      {
        Log.Error(ae);
      }

      return false;
    }

    private static HealRecord HandleHealed(string part, int optional, double beginTime)
    {
      // [Sun Feb 24 21:00:58 2019] Foob's promised interposition is fulfilled Foob healed himself for 44238 hit points by Promised Interposition Heal V. (Lucky Critical)
      // [Sun Feb 24 21:01:01 2019] Rowanoak is soothed by Brell's Soothing Wave. Farzi healed Rowanoak for 524 hit points by Brell's Sacred Soothing Wave.
      // [Sun Feb 24 21:00:52 2019] Kuvani healed Tolzol over time for 11000 hit points by Spirit of the Wood XXXIV.
      // [Sun Feb 24 21:00:52 2019] Kuvani healed Foob over time for 9409 (11000) hit points by Spirit of the Wood XXXIV.
      // [Sun Feb 24 21:00:58 2019] Fllint healed Foob for 11820 hit points by Blessing of the Ancients III.
      // [Sun Feb 24 21:01:00 2019] Tolzol healed itself for 548 hit points.
      // [Sun Feb 24 21:01:01 2019] Piemastaj`s pet has been healed for 15000 hit points by Enhanced Theft of Essence Effect X.
      // [Sun Feb 24 23:30:51 2019] Piemastaj`s pet glows with holy light. Findawenye healed Piemastaj`s pet for 2823 (78079) hit points by Mending Splash Rk. III. (Critical)
      // [Mon Feb 18 21:21:12 2019] Nylenne has been healed over time for 8211 hit points by Roar of the Lion 6.
      // [Mon Feb 18 21:20:39 2019] You have been healed over time for 1063 (8211) hit points by Roar of the Lion 6.
      // [Mon Feb 18 21:17:35 2019] Snowzz healed Malkatar over time for 8211 hit points by Roar of the Lion 6.
      // [Wed Nov 06 14:19:54 2019] Your ward heals you as it breaks! You healed Niktaza for 8970 (86306) hit points by Healing Ward. (Critical)

      HealRecord record = null;
      var test = part[..optional];

      var done = false;
      var healer = "";
      var healed = "";
      string spell = null;
      string subType = null;
      var type = Labels.Heal;
      var heal = uint.MaxValue;
      uint overHeal = 0;

      var previous = test.Length >= 2 ? test.LastIndexOf(' ', test.Length - 2) : -1;
      if (previous > -1)
      {
        if (test.IndexOf("are ", previous + 1, StringComparison.Ordinal) > -1)
        {
          done = true;
        }
        else if ((previous - 1 >= 0 && (test[previous - 1] == '.' || test[previous - 1] == '!')) || (previous - 9 > 0 &&
          test.IndexOf("fulfilled", previous - 9, StringComparison.Ordinal) > -1))
        {
          healer = test[(previous + 1)..];
        }
        else if (previous - 4 >= 0 && test.IndexOf("has been", previous - 3, StringComparison.Ordinal) > -1)
        {
          healed = test[..(previous - 4)];

          if (part.Length > optional + 17 && part.IndexOf("over time", optional + 8, 9, StringComparison.Ordinal) > -1)
          {
            type = Labels.Hot;
          }
        }
        else if (previous >= 0 && test.IndexOf("has", previous, StringComparison.Ordinal) > -1)
        {
          healer = test[..previous];
          type = Labels.Heal;
          subType = Labels.Heal;
        }
        else if (previous - 5 >= 0 && test.IndexOf("have been", previous - 4, StringComparison.Ordinal) > -1)
        {
          healed = test[..(previous - 5)];

          if (part.Length > optional + 17 && part.IndexOf("over time", optional + 8, 9, StringComparison.Ordinal) > -1)
          {
            type = Labels.Hot;
          }
        }
        else
        {
          var wardIndex = test.IndexOf("`s ward", StringComparison.OrdinalIgnoreCase);
          if (wardIndex > 0)
          {
            // assign owner of ward as healer
            healer = test[..wardIndex];
          }
        }
      }
      else
      {
        healer = test[..optional];
      }

      if (!done)
      {
        var amountIndex = -1;
        if (healed.Length == 0)
        {
          var afterHealed = optional + 8;
          var forIndex = part.IndexOf(" for ", afterHealed, StringComparison.Ordinal);

          if (forIndex > 1)
          {
            if (forIndex - 9 >= 0 && part.IndexOf("over time", forIndex - 9, StringComparison.Ordinal) > -1)
            {
              type = Labels.Hot;
              healed = part.Substring(afterHealed, forIndex - afterHealed - 10);
            }
            else
            {
              healed = part[afterHealed..forIndex];
            }

            amountIndex = forIndex + 5;
          }
        }
        else
        {
          if (type == Labels.Heal)
          {
            amountIndex = optional + 12;
          }
          else if (type == Labels.Hot)
          {
            amountIndex = optional + 22;
          }
        }

        if (amountIndex > -1)
        {
          var amountEnd = part.IndexOf(' ', amountIndex);
          if (amountEnd > -1)
          {
            var value = StatsUtil.ParseUInt(part[amountIndex..amountEnd]);
            if (value != uint.MaxValue)
            {
              heal = value;
            }

            var overEnd = -1;
            if (part.Length > amountEnd + 1 && part[amountEnd + 1] == '(')
            {
              overEnd = part.IndexOf(')', amountEnd + 2);
              if (overEnd > -1)
              {
                var value2 = StatsUtil.ParseUInt(part.AsSpan(amountEnd + 2, overEnd - amountEnd - 2));
                if (value2 != uint.MaxValue)
                {
                  overHeal = value2;
                }
              }
            }

            var rest = overEnd > -1 ? overEnd : amountEnd;
            var byIndex = part.IndexOf(" by ", rest, StringComparison.Ordinal);
            if (byIndex > -1)
            {
              var periodIndex = part.LastIndexOf('.');
              if (periodIndex > -1 && periodIndex - byIndex - 4 > 0)
              {
                spell = part.Substring(byIndex + 4, periodIndex - byIndex - 4);
              }
            }
          }
        }

        if (!string.IsNullOrEmpty(healed))
        {
          if ("You".Equals(healed, StringComparison.OrdinalIgnoreCase))
          {
            healed = ConfigUtil.PlayerName;
          }

          // check for pets
          var possessive = healed.IndexOf("`s ", StringComparison.Ordinal);
          if (possessive > -1)
          {
            if (PlayerManager.Instance.IsVerifiedPlayer(healed[..possessive]))
            {
              PlayerManager.Instance.AddVerifiedPet(healed);
            }
          }
          // found a bst/mag/nec pet
          else if (!string.IsNullOrEmpty(healer) && !string.IsNullOrEmpty(spell) && spell.StartsWith("Mend Companion", StringComparison.Ordinal))
          {
            PlayerManager.Instance.AddVerifiedPet(healed);
          }
          else if (string.IsNullOrEmpty(healer) && !string.IsNullOrEmpty(spell) && spell.StartsWith("Theft of Essence", StringComparison.OrdinalIgnoreCase))
          {
            healer = Labels.Unk;
          }

          if (!string.IsNullOrEmpty(healer) && heal != uint.MaxValue && healer.Length <= 64)
          {
            if (subType == null)
            {
              subType = string.IsNullOrEmpty(spell) ? Labels.SelfHeal : string.Intern(spell);
            }

            record = new HealRecord
            {
              Total = heal,
              OverTotal = overHeal,
              Healer = string.Intern(healer),
              Healed = string.Intern(healed),
              Type = string.Intern(type),
              ModifiersMask = -1,
              SubType = subType
            };

            if (part[^1] == ')')
            {
              // using 4 here since the shortest modifier should at least be 3 even in the future. probably.
              var firstParen = part.LastIndexOf('(', part.Length - 4);
              if (firstParen > -1)
              {
                record.ModifiersMask = LineModifiersParser.Parse(record.Healer, part.Substring(firstParen + 1, part.Length - 1 - firstParen - 1), beginTime);
                if (LineModifiersParser.IsTwincast(record.ModifiersMask))
                {
                  PlayerManager.Instance.AddVerifiedPlayer(record.Healer, beginTime);
                }
              }
            }
          }
        }
      }

      return record;
    }
  }
}
