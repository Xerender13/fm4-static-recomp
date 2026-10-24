#include <catch2/catch_test_macros.hpp>

#include "string_log.h"

using fm4_static_recomp::StringLogLine;
using fm4_static_recomp::StringShownLog;

TEST_CASE("string log lines are UTF-8") {
  CHECK(StringLogLine(u"CAR SELECT") == "String: CAR SELECT");
  CHECK(StringLogLine(u"Nürburgring") == "String: N\xc3\xbcrburgring");
  CHECK(StringLogLine(u"") == "String: ");
}

TEST_CASE("a string is logged again only after a second unseen") {
  StringShownLog log;
  CHECK(log.Shown(u"LAP", 10.0));
  CHECK_FALSE(log.Shown(u"LAP", 10.5));
  CHECK_FALSE(log.Shown(u"LAP", 11.4));
  CHECK(log.Shown(u"CAR SELECT", 11.4));
  CHECK(log.Shown(u"LAP", 12.5));
}
