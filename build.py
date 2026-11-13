#!/usr/bin/env python3
"""
Build script for PeakMediaMcpServers
Supports: build, run, start, stop, status, test, test-live, smoke, mcp, log commands
"""

import sys
import os

# Fix Unicode output on Windows
if sys.platform == 'win32':
    sys.stdout.reconfigure(encoding='utf-8', errors='replace')
    sys.stderr.reconfigure(encoding='utf-8', errors='replace')
import platform
import subprocess
import signal
import time
import json
import queue
import tempfile
import threading
import urllib.error
import xml.etree.ElementTree as ElementTree
import urllib.request
from pathlib import Path
from typing import Optional

try:
    import psutil
except ImportError:
    print("Error: psutil is required. Create the venv: python3 -m venv .venv && .venv/bin/pip install psutil fastmcp")
    sys.exit(1)

# Optional: fastmcp for MCP client functionality
FASTMCP_AVAILABLE = False
try:
    import asyncio
    from fastmcp import Client as McpClient
    FASTMCP_AVAILABLE = True
except ImportError:
    pass

# Project configuration
SOLUTION_PATH = "PeakMediaMcpServers.sln"
PROJECT_PATH = "src/PeakMediaRemoteMcpServer/PeakMediaRemoteMcpServer.csproj"
PID_FILE = Path(".peak-media-mcp.pid")
# Console output of the background process (build messages, crashes before logging starts).
CONSOLE_FILE = Path("logs/peak-media-mcp.console.log")
# The gateway's own daily log files (Serilog), next to the binary; the date in the name is UTC.
LOG_DIR = Path("src/PeakMediaRemoteMcpServer/bin/Debug/net11.0/logs")
LOG_GLOB = "PeakMediaRemoteMcpServer_*.log"
DEV_PORT = 5108
# A gateway access token for `build.py mcp` (sent as a Bearer token, never printed).
ACCESS_TOKEN_VARIABLE = "PEAK_MEDIA_ACCESS_TOKEN"
# The live OpenRouter gate: a real key (never printed), falling back to OpenRouter__ApiKey in .env.
LIVE_KEY_VARIABLE = "PEAK_MEDIA_LIVE_OPENROUTER_API_KEY"
LIVE_TEST_PROJECT = "tests/PeakMediaRemoteMcpServer.Tests/PeakMediaRemoteMcpServer.Tests.csproj"
LIVE_TEST_FILTER = "Category=LiveOpenRouter"
ENV_FILE = Path(".env")
# `build.py smoke`: the test sign-in tool (stub OIDC issuer + the gateway's real sign-in flow).
STUB_SIGN_IN_PROJECT = "tests/StubSignIn/PeakMediaStubSignIn.csproj"


def latest_log_file() -> Optional[Path]:
    """Newest daily log file written by the gateway, if any."""
    files = sorted(LOG_DIR.glob(LOG_GLOB))
    return files[-1] if files else None


def is_process_running(pid: int) -> bool:
    """Check if a process with given PID is running"""
    try:
        process = psutil.Process(pid)
        return process.is_running() and process.status() != psutil.STATUS_ZOMBIE
    except (psutil.NoSuchProcess, psutil.AccessDenied):
        return False


def get_running_pid() -> Optional[int]:
    """Get PID of running application if it exists"""
    if not PID_FILE.exists():
        return None
    
    try:
        with open(PID_FILE, 'r') as f:
            pid = int(f.read().strip())
        
        if is_process_running(pid):
            return pid
        else:
            # Clean up stale PID file
            PID_FILE.unlink()
            return None
    except (ValueError, IOError):
        return None


def check_status() -> None:
    """Check if application is running"""
    pid = get_running_pid()
    
    if pid:
        print(f"✓ Application is running (PID: {pid})")
        print(f"  URL: http://localhost:{DEV_PORT}")
        print(f"  MCP: http://localhost:{DEV_PORT}/mcp")
        print(f"  Log file: {latest_log_file() or LOG_DIR}")
    else:
        print("✗ Application is not running")


def start_background(extra_env: Optional[dict] = None) -> bool:
    """Start application in background; extra_env overrides the environment. True once /version answers."""
    # Check if already running
    existing_pid = get_running_pid()
    if existing_pid:
        print(f"Application is already running (PID: {existing_pid})")
        print("Use 'python build.py stop' to stop it first")
        return False
    
    print("Starting application in background...")
    
    # Console output goes to a file; the gateway itself logs to stderr and its daily log file.
    CONSOLE_FILE.parent.mkdir(parents=True, exist_ok=True)
    log_file = open(CONSOLE_FILE, 'w')
    
    # Set environment for Development mode
    env = os.environ.copy()
    env["ASPNETCORE_ENVIRONMENT"] = "Development"
    if extra_env:
        env.update(extra_env)
    
    # Start process
    if platform.system() == "Windows":
        # On Windows, use CREATE_NEW_PROCESS_GROUP to prevent Ctrl+C propagation
        process = subprocess.Popen(
            ["dotnet", "run", "--project", PROJECT_PATH, "--no-restore"],
            stdout=log_file,
            stderr=subprocess.STDOUT,
            env=env,
            creationflags=subprocess.CREATE_NEW_PROCESS_GROUP
        )
    else:
        # On Unix, use start_new_session
        process = subprocess.Popen(
            ["dotnet", "run", "--project", PROJECT_PATH, "--no-restore"],
            stdout=log_file,
            stderr=subprocess.STDOUT,
            env=env,
            start_new_session=True
        )
    
    # Save PID
    with open(PID_FILE, 'w') as f:
        f.write(str(process.pid))
    
    print(f"Application started (PID: {process.pid})")
    print(f"Console output: {CONSOLE_FILE}")

    # Wait for GET /version to answer (or the process to exit, e.g. a startup refusal)
    deadline = time.time() + 30
    while time.time() < deadline:
        if not is_process_running(process.pid):
            break
        try:
            with urllib.request.urlopen(f"http://localhost:{DEV_PORT}/version", timeout=2) as response:
                print(f"✓ Application is running at http://localhost:{DEV_PORT}")
                print(f"  Version: {response.read().decode('utf-8')}")
                print(f"  MCP endpoint: http://localhost:{DEV_PORT}/mcp (sign-in required)")
                return True
        except (urllib.error.URLError, ConnectionError, TimeoutError):
            time.sleep(0.5)

    print("✗ Application failed to start. Last console output:")
    try:
        lines = CONSOLE_FILE.read_text(encoding='utf-8', errors='replace').splitlines()
        for line in lines[-15:]:
            print(f"  {line}")
    except OSError:
        pass
    print("  Gateway refuses to start without McpAuth enabled, Broker__UpstreamClientSecret and OpenRouter__ApiKey.")
    if PID_FILE.exists():
        PID_FILE.unlink()
    return False


def stop_background() -> None:
    """Stop background application"""
    pid = get_running_pid()
    
    if not pid:
        print("No running application found")
        return
    
    print(f"Stopping application (PID: {pid})...")
    
    try:
        process = psutil.Process(pid)
        
        # Try graceful shutdown first
        if platform.system() == "Windows":
            process.terminate()
        else:
            process.send_signal(signal.SIGTERM)
        
        # Wait up to 10 seconds for graceful shutdown
        try:
            process.wait(timeout=10)
            print("✓ Application stopped gracefully")
        except psutil.TimeoutExpired:
            # Force kill if still running
            print("Application did not stop gracefully, forcing shutdown...")
            process.kill()
            process.wait(timeout=5)
            print("✓ Application stopped forcefully")
        
        # Clean up PID file
        if PID_FILE.exists():
            PID_FILE.unlink()
    
    except psutil.NoSuchProcess:
        print("Process already stopped")
        if PID_FILE.exists():
            PID_FILE.unlink()
    
    except Exception as e:
        print(f"Error stopping application: {e}")
        sys.exit(1)


def read_env_file_value(name: str) -> str:
    """The value of `name` in .env (KEY=VALUE lines, optional `export`, optional quotes), or ''."""
    if not ENV_FILE.exists():
        return ""
    for line in ENV_FILE.read_text(encoding="utf-8").splitlines():
        line = line.strip()
        if not line or line.startswith("#") or "=" not in line:
            continue
        key, value = line.split("=", 1)
        key = key.strip()
        if key.startswith("export "):
            key = key[len("export "):].strip()
        if key == name:
            return value.strip().strip('"').strip("'")
    return ""


def run_live_tests() -> int:
    """Run the live OpenRouter test; fail unless it actually ran and passed (a skip is a failure)."""
    key = os.environ.get(LIVE_KEY_VARIABLE, "").strip()
    source = LIVE_KEY_VARIABLE
    if not key:
        key = read_env_file_value("OpenRouter__ApiKey")
        source = "OpenRouter__ApiKey in .env"
    if not key:
        print(f"Error: no OpenRouter key for the live test. Set {LIVE_KEY_VARIABLE} or put OpenRouter__ApiKey in .env, then rerun.",
              file=sys.stderr)
        return 1

    print(f"Running the live OpenRouter test (real spend, about $0.001; key from {source})...")
    env = os.environ.copy()
    env[LIVE_KEY_VARIABLE] = key
    with tempfile.TemporaryDirectory(prefix="peakmedia-live-") as results_dir:
        completed = subprocess.run(
            ["dotnet", "test", LIVE_TEST_PROJECT, "--filter", LIVE_TEST_FILTER,
             "--logger", "trx;LogFileName=live.trx", "--results-directory", results_dir],
            env=env,
        )
        trx = Path(results_dir) / "live.trx"
        if not trx.exists():
            print("Error: the live test produced no results. Check the build output above and rerun.", file=sys.stderr)
            return 1
        counters = next((e for e in ElementTree.parse(trx).iter() if e.tag.endswith("Counters")), None)
        if counters is None:
            print("Error: the live test results could not be read. Rerun build.py test-live.", file=sys.stderr)
            return 1
        passed = int(counters.get("passed", "0"))
        failed = int(counters.get("failed", "0")) + int(counters.get("error", "0"))
        skipped = int(counters.get("notExecuted", "0"))

    print(f"Live OpenRouter: {passed} passed, {failed} failed, {skipped} skipped")
    if completed.returncode != 0 or failed > 0:
        print("Error: the live OpenRouter test failed. Read the failure above; the real service disagrees with the gateway.",
              file=sys.stderr)
        return 1
    if passed == 0 or skipped > 0:
        print(f"Error: the live OpenRouter test did not run (skipped or not found). Check {LIVE_KEY_VARIABLE} and the test filter.",
              file=sys.stderr)
        return 1
    print("✓ Live OpenRouter gate passed")
    return 0


def read_stub_line(lines: "queue.Queue", stub: subprocess.Popen, prefix: str, timeout: float = 60) -> Optional[str]:
    """The value of the next `prefix=value` line from the sign-in tool, or None on exit or timeout."""
    deadline = time.time() + timeout
    while time.time() < deadline:
        try:
            line = lines.get(timeout=0.5)
        except queue.Empty:
            if stub.poll() is not None and lines.empty():
                return None
            continue
        if line is None:
            return None
        if line.startswith(prefix + "="):
            return line[len(prefix) + 1:]
        if line.startswith("error="):
            print(f"✗ Test sign-in failed at {line[len('error='):]}.", file=sys.stderr)
            return None
    return None


def stop_stub(stub: subprocess.Popen) -> None:
    """Close the sign-in tool's stdin (it exits), or kill it and its children."""
    try:
        if stub.stdin and not stub.stdin.closed:
            stub.stdin.close()
    except OSError:
        pass
    try:
        stub.wait(timeout=10)
        return
    except subprocess.TimeoutExpired:
        pass
    try:
        parent = psutil.Process(stub.pid)
        for child in parent.children(recursive=True):
            child.kill()
        parent.kill()
    except psutil.NoSuchProcess:
        pass
    stub.wait(timeout=5)


def run_smoke() -> int:
    """Run-the-tool gate without a browser: start the gateway against the test sign-in server (stub OIDC
    issuer in place of Okta), sign in through the gateway's real flow, list the tools, stop everything."""
    existing_pid = get_running_pid()
    if existing_pid:
        print(f"✗ The application is already running (PID: {existing_pid}). Stop it first with: python build.py stop",
              file=sys.stderr)
        return 1

    openrouter_key = os.environ.get("OpenRouter__ApiKey", "").strip() or read_env_file_value("OpenRouter__ApiKey")
    if not openrouter_key:
        print("✗ OpenRouter__ApiKey is not set. Export it or put it in .env, then rerun build.py smoke.", file=sys.stderr)
        return 1

    print("Building solution...")
    if subprocess.run(["dotnet", "build", SOLUTION_PATH]).returncode != 0:
        print("✗ Build failed. Fix the errors above and rerun build.py smoke.", file=sys.stderr)
        return 1

    print("Starting the test sign-in server (stub OIDC issuer)...")
    stub = subprocess.Popen(
        ["dotnet", "run", "--no-build", "--project", STUB_SIGN_IN_PROJECT],
        stdin=subprocess.PIPE, stdout=subprocess.PIPE, text=True, bufsize=1)
    lines: "queue.Queue" = queue.Queue()

    def pump() -> None:
        for raw in stub.stdout:
            lines.put(raw.strip())
        lines.put(None)

    threading.Thread(target=pump, daemon=True).start()
    gateway_started = False
    try:
        authority = read_stub_line(lines, stub, "authority")
        client_id = read_stub_line(lines, stub, "client_id")
        client_secret = read_stub_line(lines, stub, "client_secret")
        if not (authority and client_id and client_secret):
            print("✗ The test sign-in server did not start. Rerun build.py smoke; if it repeats, run "
                  f"dotnet run --project {STUB_SIGN_IN_PROJECT} to see why.", file=sys.stderr)
            return 1
        print(f"  Test sign-in server at {authority}")

        # Only this gateway process sees the stub settings; the real Okta settings stay as they are elsewhere.
        gateway_started = start_background(extra_env={
            "Broker__UpstreamAuthority": authority,
            "Broker__UpstreamClientId": client_id,
            "Broker__UpstreamClientSecret": client_secret,
            "Broker__UpstreamGroupsClaim": "groups",
            "OpenRouter__ApiKey": openrouter_key,
        })
        if not gateway_started:
            print("✗ The gateway did not start against the test sign-in server. Read the console output above, "
                  "fix it, and rerun build.py smoke.", file=sys.stderr)
            return 1

        print("Signing in through the gateway (test sign-in server, no browser)...")
        stub.stdin.write(f"signin http://localhost:{DEV_PORT}\n")
        stub.stdin.flush()
        token = read_stub_line(lines, stub, "token")
        if not token:
            print(f"✗ Sign-in through the gateway failed. Check build.py log --level warn and {CONSOLE_FILE}, "
                  "then rerun build.py smoke.", file=sys.stderr)
            return 1
        print("✓ Signed in as smoke@example.com")

        os.environ[ACCESS_TOKEN_VARIABLE] = token
        try:
            result = run_mcp("tools")
        finally:
            os.environ.pop(ACCESS_TOKEN_VARIABLE, None)
        if result != 0:
            print("✗ The tool list did not come back. Read the MCP error above and the gateway log "
                  "(build.py log --level warn), then rerun build.py smoke.", file=sys.stderr)
            return 1
        print("\n✓ Smoke check passed: signed in through the test sign-in server and listed the tools")
        return 0
    finally:
        if gateway_started or get_running_pid():
            stop_background()
        stop_stub(stub)


def run_dotnet_command(command: str) -> None:
    """Execute the appropriate dotnet command"""
    try:
        if command == "run":
            print("Running project in foreground...")
            print(f"URL: http://localhost:{DEV_PORT}")
            print("Press Ctrl+C to stop...")
            
            # Set environment for Development mode
            env = os.environ.copy()
            env["ASPNETCORE_ENVIRONMENT"] = "Development"
            
            subprocess.run(
                ["dotnet", "run", "--project", PROJECT_PATH, "--no-restore"],
                env=env
            )
        
        elif command == "build":
            # Auto-stop any running application to prevent file lock issues
            pid = get_running_pid()
            if pid:
                print("Stopping running application before build...")
                stop_background()
            
            print("Building solution...")
            subprocess.run(
                ["dotnet", "build", SOLUTION_PATH],
                check=True
            )
            print("✓ Successfully built solution")
        
        elif command == "start":
            # Auto-stop any running application to prevent file lock issues
            pid = get_running_pid()
            if pid:
                print("Stopping running application before build...")
                stop_background()
            
            # Auto-build solution before starting
            print("Building solution before start...")
            subprocess.run(
                ["dotnet", "build", SOLUTION_PATH],
                check=True
            )
            start_background()
        
        elif command == "stop":
            stop_background()

        elif command == "status":
            check_status()

        elif command == "test":
            print("Running test suite...")
            subprocess.run(
                ["dotnet", "test", SOLUTION_PATH],
                check=True
            )
            print("✓ Test suite passed")

        else:
            print(f"Unknown command: {command}")
            print_usage()
            sys.exit(1)
    
    except KeyboardInterrupt:
        # Handle Ctrl+C gracefully for interactive commands
        print("\n\nShutdown requested. Exiting cleanly...")
        sys.exit(0)
    
    except subprocess.CalledProcessError:
        print(f"Error: Failed to {command} project")
        sys.exit(1)


def search_log(pattern: Optional[str] = None, tail: int = 0, level: Optional[str] = None) -> None:
    """Search or tail the log file
    
    Args:
        pattern: Regex pattern to search for (case-insensitive)
        tail: Number of lines to show from end (0 = all)
        level: Filter by log level (error, warn, info, debug)
    """
    import re
    
    log_path = latest_log_file()
    if log_path is None:
        print(f"No log file found under {LOG_DIR}")
        print("Start the application first with: python build.py start")
        return

    try:
        with open(log_path, 'r', encoding='utf-8', errors='replace') as f:
            lines = f.readlines()
        
        # Apply tail
        if tail > 0:
            lines = lines[-tail:]
        
        # Filter by level if specified
        if level:
            level_upper = level.upper()
            level_codes = {"VERBOSE": "VRB", "DEBUG": "DBG", "INFO": "INF", "INFORMATION": "INF",
                           "WARN": "WRN", "WARNING": "WRN", "ERROR": "ERR", "FATAL": "FTL"}
            code = level_codes.get(level_upper, level_upper[:3])
            level_pattern = re.compile(rf'\[{code}\]')
            lines = [line for line in lines if level_pattern.search(line)]
        
        # Filter by pattern if specified
        if pattern:
            try:
                regex = re.compile(pattern, re.IGNORECASE)
                lines = [line for line in lines if regex.search(line)]
            except re.error as e:
                print(f"Invalid regex pattern: {e}")
                return
        
        if lines:
            for line in lines:
                print(line.rstrip())
            print(f"\n--- {len(lines)} line(s) shown ---")
        else:
            print("No matching log entries found")
    
    except Exception as e:
        print(f"Error reading log file: {e}")


async def run_mcp_command(subcommand: str, tool_name: Optional[str] = None, 
                          tool_args: Optional[str] = None, timeout: int = 200,
                          oauth: bool = False) -> int:
    """Execute MCP client commands against the running server.
    
    Args:
        subcommand: The MCP subcommand (tools, call, ping, info)
        tool_name: Name of the tool to call (for 'call' subcommand)
        tool_args: JSON string of arguments for tool call
        timeout: Timeout in seconds for operations (default: 200)
        oauth: Sign in through the browser with fastmcp's OAuth client
    
    A gateway access token in PEAK_MEDIA_ACCESS_TOKEN is sent as a Bearer token (never printed).
    
    Returns:
        Exit code (0 for success, 1 for error)
    """
    if not FASTMCP_AVAILABLE:
        print("✗ fastmcp package is not installed")
        print("\nInstall it with:")
        print("  .venv/bin/pip install fastmcp")
        return 1
    
    # Check if server is running
    pid = get_running_pid()
    if not pid:
        print("✗ Application is not running")
        print("Start it first with: python build.py start")
        return 1
    
    mcp_url = f"http://localhost:{DEV_PORT}/mcp"

    # /mcp is behind sign-in: use a gateway access token from the environment, or sign in through the
    # browser (--oauth). Without either the server answers 401; say so plainly.
    access_token = os.environ.get(ACCESS_TOKEN_VARIABLE, "").strip()
    auth = access_token if access_token else ("oauth" if oauth else None)
    if auth is None and probe_mcp_requires_sign_in(mcp_url):
        print(f"✗ {mcp_url} requires sign-in (HTTP 401).")
        print(f"  Sign in through the browser with: python build.py mcp {subcommand} --oauth")
        print(f"  or export a gateway access token in {ACCESS_TOKEN_VARIABLE} and retry.")
        return 1

    try:
        # Pass timeout to client constructor - this applies to all MCP operations
        client = McpClient(mcp_url, timeout=timeout, auth=auth)
        
        async with client:
            if subcommand == "ping":
                await client.ping()
                print(f"✓ MCP server is reachable at {mcp_url}")
                return 0
            
            elif subcommand == "info":
                # Show server info from initialization
                init_result = client.initialize_result
                if init_result is None and getattr(client, "server_info", None) is not None:
                    # fastmcp 4 may connect through server/discover instead of initialize.
                    print("MCP Server Information:")
                    print(f"  Name: {client.server_info.name}")
                    print(f"  Version: {client.server_info.version}")
                    return 0
                if init_result:
                    print("MCP Server Information:")
                    print(f"  Name: {init_result.serverInfo.name}")
                    print(f"  Version: {init_result.serverInfo.version}")
                    if init_result.instructions:
                        print(f"  Instructions: {init_result.instructions}")
                    if init_result.capabilities:
                        caps = init_result.capabilities
                        print("  Capabilities:")
                        if caps.tools:
                            print("    - Tools: ✓")
                        if caps.resources:
                            print("    - Resources: ✓")
                        if caps.prompts:
                            print("    - Prompts: ✓")
                return 0
            
            elif subcommand == "tools":
                tools = await client.list_tools()
                if not tools:
                    print("No tools available")
                    return 0
                
                print(f"Available MCP Tools ({len(tools)}):")
                print("-" * 60)
                for tool in tools:
                    print(f"\n  {tool.name}")
                    if tool.description:
                        # Wrap description nicely
                        desc_lines = tool.description.split('\n')
                        for line in desc_lines[:3]:  # Show first 3 lines
                            print(f"    {line.strip()}")
                        if len(desc_lines) > 3:
                            print(f"    ...")
                    if tool.inputSchema:
                        props = tool.inputSchema.get('properties', {})
                        required = tool.inputSchema.get('required', [])
                        if props:
                            print("    Parameters:")
                            for param, schema in props.items():
                                req_marker = "*" if param in required else ""
                                param_type = schema.get('type', 'any')
                                param_desc = schema.get('description', '')
                                print(f"      - {param}{req_marker}: {param_type}")
                                if param_desc:
                                    desc_truncated = param_desc[:60] + "..." if len(param_desc) > 60 else param_desc
                                    print(f"          {desc_truncated}")
                return 0
            
            elif subcommand == "call":
                if not tool_name:
                    print("✗ Tool name is required for 'call' subcommand")
                    print("Usage: python build.py mcp call <tool_name> ['{\"arg\": \"value\"}']")
                    return 1
                
                # Parse tool arguments
                args_dict = {}
                if tool_args:
                    try:
                        args_dict = json.loads(tool_args)
                    except json.JSONDecodeError as e:
                        print(f"✗ Invalid JSON arguments: {e}")
                        return 1
                
                print(f"Calling tool: {tool_name}")
                if args_dict:
                    print(f"Arguments: {json.dumps(args_dict, indent=2)}")
                
                result = await client.call_tool(tool_name, args_dict)
                
                print("\nResult:")
                print("-" * 40)
                # Handle different result types
                if hasattr(result, 'content'):
                    for content in result.content:
                        if hasattr(content, 'text'):
                            print(content.text)
                        else:
                            print(content)
                else:
                    print(result)
                return 0
            
            else:
                print(f"✗ Unknown MCP subcommand: {subcommand}")
                print("Available subcommands: ping, info, tools, call")
                return 1
    
    except Exception as e:
        print(f"✗ MCP Error: {e}")
        return 1


def probe_mcp_requires_sign_in(mcp_url: str) -> bool:
    """True when an unauthenticated initialize request to /mcp is answered with 401."""
    body = json.dumps({"jsonrpc": "2.0", "id": 1, "method": "initialize",
                       "params": {"protocolVersion": "2025-06-18", "capabilities": {},
                                  "clientInfo": {"name": "build.py", "version": "1"}}}).encode("utf-8")
    request = urllib.request.Request(mcp_url, data=body, method="POST", headers={
        "Content-Type": "application/json", "Accept": "application/json, text/event-stream"})
    try:
        with urllib.request.urlopen(request, timeout=10):
            return False
    except urllib.error.HTTPError as e:
        return e.code == 401
    except (urllib.error.URLError, ConnectionError, TimeoutError):
        return False


def run_mcp(subcommand: str, tool_name: Optional[str] = None, 
            tool_args: Optional[str] = None, timeout: int = 200, oauth: bool = False) -> int:
    """Synchronous wrapper for run_mcp_command."""
    return asyncio.run(run_mcp_command(subcommand, tool_name, tool_args, timeout, oauth))


def print_usage() -> None:
    """Print usage information"""
    print("Usage: python build.py [command] [options]")
    print("")
    print("Build & Run Commands:")
    print("  build        - Build the solution (auto-stops running app)")
    print("  run          - Run the project in foreground (Ctrl+C to stop)")
    print("  start        - Build and start in background (port 5108)")
    print("  stop         - Stop the background application")
    print("  status       - Check if application is running")
    print("  test         - Run the test suite (dotnet test)")
    print("  test-live    - Run the live OpenRouter test (real spend; fails if skipped)")
    print("  smoke        - Start the gateway against the test sign-in server, sign in (no browser),")
    print("                 list the MCP tools, then stop everything (the Run-the-tool gate)")
    print("")
    print("MCP Commands (requires the .venv with fastmcp; /mcp needs sign-in):")
    print("  mcp ping                 - Check MCP server connectivity")
    print("  mcp info                 - Show MCP server information")
    print("  mcp tools                - List available MCP tools")
    print("  mcp call <tool> ['{...}'] - Call an MCP tool with JSON args")
    print("  mcp <subcommand> --oauth - Sign in through the browser first (real Okta — release check)")
    print(f"  {ACCESS_TOKEN_VARIABLE}=<token> - Use this gateway access token instead (never printed)")
    print("")
    print("Log Commands:")
    print("  log                      - Show last 50 lines of log")
    print("  log <pattern>            - Search log for regex pattern")
    print("  log --tail <n>           - Show last n lines")
    print("  log --level <level>      - Filter by level (error/warn/info/debug)")
    print("  log --tail 100 --level error - Combine options")
    print("")
    print("URLs (when running):")
    print(f"  http://localhost:{DEV_PORT}/mcp          - MCP Streamable HTTP endpoint")
    print("")
    print(f"  http://localhost:{DEV_PORT}/version      - Name and version (no sign-in)")
    print("")
    print("Environment (gateway refuses to start without these):")
    print("  Broker__UpstreamClientSecret, OpenRouter__ApiKey")
    print("")
    print("Examples:")
    print("  python build.py start                       # Build and start server")
    print("  python build.py smoke                       # Sign in (test server) and list the tools")
    print("  python build.py mcp tools                   # List all MCP tools")
    print("  python build.py log --level error           # View error logs")
    print("  python build.py stop                        # Stop the server")


def main() -> None:
    """Main entry point"""
    # Parse command argument
    command = sys.argv[1] if len(sys.argv) > 1 else "build"
    
    # Special commands that don't need dotnet
    if command in ["stop", "status"]:
        if command == "stop":
            stop_background()
        else:
            check_status()
        return
    
    # Log command
    if command == "log":
        pattern = None
        tail = 50  # Default to last 50 lines
        level = None
        
        # Parse log options
        args = sys.argv[2:]
        i = 0
        while i < len(args):
            if args[i] == "--tail" and i + 1 < len(args):
                try:
                    tail = int(args[i + 1])
                except ValueError:
                    print(f"Invalid tail value: {args[i + 1]}")
                    sys.exit(1)
                i += 2
            elif args[i] == "--level" and i + 1 < len(args):
                level = args[i + 1]
                i += 2
            elif not args[i].startswith("--"):
                pattern = args[i]
                i += 1
            else:
                print(f"Unknown option: {args[i]}")
                print_usage()
                sys.exit(1)
        
        search_log(pattern=pattern, tail=tail, level=level)
        return
    
    # MCP command
    if command == "mcp":
        if len(sys.argv) < 3:
            print("Error: mcp requires a subcommand")
            print("Usage: python build.py mcp <ping|info|tools|call> [args]")
            sys.exit(1)
        
        oauth = "--oauth" in sys.argv[2:]
        mcp_args = [a for a in sys.argv[2:] if a != "--oauth"]
        subcommand = mcp_args[0] if mcp_args else ""
        tool_name = None
        tool_args = None
        
        # For 'call' subcommand, parse tool name and optional args
        if subcommand == "call":
            if len(mcp_args) >= 2:
                tool_name = mcp_args[1]
            if len(mcp_args) >= 3:
                tool_args = mcp_args[2]
        
        sys.exit(run_mcp(subcommand, tool_name, tool_args, oauth=oauth))
    
    # Run-the-tool gate through the test sign-in server
    if command == "smoke":
        sys.exit(run_smoke())

    # Live upstream gate
    if command == "test-live":
        sys.exit(run_live_tests())

    # Help command
    if command in ["help", "--help", "-h"]:
        print_usage()
        return
    
    # Commands that need dotnet
    if command not in ["build", "run", "start", "test"]:
        print(f"Unknown command: {command}")
        print_usage()
        sys.exit(1)
    
    # Execute command
    run_dotnet_command(command)


if __name__ == "__main__":
    main()
