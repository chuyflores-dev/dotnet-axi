def tools: ["command_execution", "file_change", "mcp_tool_call", "web_search"];
def raw_executables:
  ["cat", "sed", "head", "tail", "bat", "less", "more", "nl",
   "get-content", "get-childitem", "gci", "select-string", "sls",
   "rg", "grep", "find", "fd", "ls", "tree"];
def unquote:
  tostring
  | if ((startswith("\"") and endswith("\""))
        or (startswith("'") and endswith("'"))) and length >= 2 then
      .[1:-1]
    else
      .
    end;
def executable_name:
  unquote
  | gsub("\\\\"; "/")
  | split("/")[-1]
  | ascii_downcase
  | sub("\\.exe$"; "");
def command_argv:
  (.item.command // []) | map(tostring);
def wrapped_shell_command:
  command_argv as $argv
  | ($argv[0] // "" | executable_name) as $executable
  | ($argv[1] // "") as $switch
  | ($switch | ascii_downcase) as $normalizedSwitch
  | if ($argv | length) == 1 then
      $argv[0]
    elif ($argv | length) >= 3 and
         (["sh", "bash", "dash", "zsh"] | index($executable)) != null and
         ($switch == "-c" or $switch == "-lc") then
      $argv[2]
    elif ($argv | length) >= 3 and
         (["pwsh", "powershell"] | index($executable)) != null and
         ($normalizedSwitch == "-command" or $normalizedSwitch == "-c") then
      $argv[2]
    elif ($argv | length) >= 3 and
         $executable == "cmd" and $normalizedSwitch == "/c" then
      $argv[2]
    else
      ""
    end;
def shell_segments($command):
  reduce ($command | tostring | explode[]) as $code (
    {segments: [], current: [], quote: 0, escaped: false};
    if .escaped then
      .current += [$code] | .escaped = false
    elif $code == 92 and .quote != 39 then
      .current += [$code] | .escaped = true
    elif $code == 39 and .quote != 34 then
      .current += [$code]
      | .quote = (if .quote == 39 then 0 else 39 end)
    elif $code == 34 and .quote != 39 then
      .current += [$code]
      | .quote = (if .quote == 34 then 0 else 34 end)
    elif .quote == 0 and
         ($code == 10 or $code == 13 or $code == 38 or
          $code == 59 or $code == 124) then
      .segments += [(.current | implode)] | .current = []
    else
      .current += [$code]
    end
  )
  | (.segments + [(.current | implode)])
  | map(gsub("^[[:space:]]+|[[:space:]]+$"; ""))
  | map(select(length > 0));
def string_executable_prefix:
  "^[[:space:]]*(?:(?:\"[^\"]*[\\\\/])|(?:[^[:space:]\";|&]*[\\\\/])|\"?)";
def raw_shell_command($command):
  shell_segments($command)
  | any(.[];
      test(
        string_executable_prefix
        + "(cat|sed|head|tail|bat|less|more|nl|get-content|get-childitem|gci|select-string|sls|rg|grep|find|fd|ls|tree)(?:\\.exe)?\"?"
        + "([[:space:]]|$)";
        "i")
      or test(
        string_executable_prefix
        + "git(?:\\.exe)?\"?[[:space:]]+(show|grep)([[:space:]]|$)";
        "i"));
def raw_argv:
  command_argv as $argv
  | ($argv[0] // "" | executable_name) as $executable
  | ((raw_executables | index($executable)) != null)
    or ($executable == "git" and
        (($argv[1] // "" | ascii_downcase) == "show" or
         ($argv[1] // "" | ascii_downcase) == "grep"));
def raw_repository_read:
  . as $event
  | if (.item.command | type) == "array" then
      raw_argv
      or (($event | wrapped_shell_command) as $command
          | ($command != "" and raw_shell_command($command)))
    else
      raw_shell_command(.item.command // "")
    end;
def segment_dnx_dnaxi($segment; $version):
  [
    $segment
    | match(
        string_executable_prefix
        + "dnx(?:\\.exe)?\"?[[:space:]]+(?<package>[^[:space:]]+)"
        + "([[:space:]]|$)";
        "gi")
    | .captures[]
    | select(.name == "package")
    | .string
    | unquote
  ]
  | any(.[]; . == ("dnaxi@" + $version));
def dnaxi_shell_command($command; $version):
  shell_segments($command)
  | any(.[];
      segment_dnx_dnaxi(.; $version)
      or test(
        string_executable_prefix
        + "(dnaxi|dotnet-dnaxi)(?:\\.exe)?\"?([[:space:]]|$)";
        "i")
      or test(
        string_executable_prefix
        + "dotnet(?:\\.exe)?\"?[[:space:]]+tool[[:space:]]+run[[:space:]]+dnaxi"
        + "([[:space:]]|$)";
        "i"));
def dnaxi_argv($version):
  command_argv as $argv
  | ($argv[0] // "" | executable_name) as $executable
  | ($executable == "dnx" and
      ($argv[1] // "" | unquote) == ("dnaxi@" + $version))
    or ($executable == "dnaxi")
    or ($executable == "dotnet-dnaxi")
    or ($executable == "dotnet" and
        ($argv[1] // "" | ascii_downcase) == "tool" and
        ($argv[2] // "" | ascii_downcase) == "run" and
        ($argv[3] // "" | ascii_downcase | unquote) == "dnaxi");
def dnaxi_command($version):
  . as $event
  | if (.item.command | type) == "array" then
      dnaxi_argv($version)
      or (($event | wrapped_shell_command) as $command
          | ($command != "" and dnaxi_shell_command($command; $version)))
    else
      dnaxi_shell_command(.item.command // ""; $version)
    end;
{
  inputTokens: ([.[] | select(.type=="turn.completed") | .usage.input_tokens // 0] | add // 0),
  cachedInputTokens: ([.[] | select(.type=="turn.completed") | .usage.cached_input_tokens // 0] | add // 0),
  cacheWriteInputTokens: ([.[] | select(.type=="turn.completed") | .usage.cache_write_input_tokens // 0] | add // 0),
  outputTokens: ([.[] | select(.type=="turn.completed") | .usage.output_tokens // 0] | add // 0),
  reasoningOutputTokens: ([.[] | select(.type=="turn.completed") | .usage.reasoning_output_tokens // 0] | add // 0),
  turns: ([.[] | select(.type=="turn.started")] | length),
  toolCalls: ([.[] | select((.type=="item.started" or .type=="item.completed") and (.item.type as $t | tools | index($t))) | .item.id] | unique | length),
  rawRepositoryReadCommandCount: ([.[] | select((.type=="item.started" or .type=="item.completed") and .item.type=="command_execution" and raw_repository_read) | .item.id] | unique | length),
  dnaxiInvocations: ([.[] | select((.type=="item.started" or .type=="item.completed") and .item.type=="command_execution" and dnaxi_command($version)) | .item.id] | unique | length),
  dnaxiSuccessfulInvocations: ([.[] | select(.type=="item.completed" and .item.type=="command_execution" and dnaxi_command($version) and (.item.exit_code | type)=="number" and .item.exit_code==0) | .item.id] | unique | length),
  dnaxiNonzeroExits: ([.[] | select(.type=="item.completed" and .item.type=="command_execution" and dnaxi_command($version) and (.item.exit_code | type)=="number" and .item.exit_code!=0) | .item.id] | unique | length)
}
