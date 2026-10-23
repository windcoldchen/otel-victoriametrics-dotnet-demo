#!/usr/bin/env bash
set -euo pipefail

echo "================================================================"
echo " 🚀 本地模擬標準 CI 流水線驗證 (Local Standard CI Runner)"
echo "================================================================"

# 1. 執行靜態語法檢查
echo -e "\n[階段 1/4] 執行配置與語法檢查 (Syntax & Config Validation)..."
./scripts/validate-syntax.sh

# 2. 執行 .NET 10 程式碼格式與風格檢查
echo -e "\n[階段 2/4] 執行 C# 程式碼格式與規範檢查 (Code Style & Format)..."
DOCKER_CMD="docker"
if [ -S "/Users/luke.chen/.docker/run/docker.sock" ]; then
  export DOCKER_HOST="unix:///Users/luke.chen/.docker/run/docker.sock"
fi

$DOCKER_CMD run --rm -v "$PWD":/src -w /src mcr.microsoft.com/dotnet/sdk:10.0 \
  dotnet format OrderService.sln --verify-no-changes --verbosity normal
echo "  ✅ C# 格式化檢查通過 (Code format verified)"

# 3. 執行單元測試與覆蓋率收集
echo -e "\n[階段 3/4] 執行單元測試與覆蓋率收集 (Unit Tests & Coverage)..."
$DOCKER_CMD run --rm -v "$PWD":/src -w /src mcr.microsoft.com/dotnet/sdk:10.0 \
  dotnet test OrderService.sln -c Release --collect:"XPlat Code Coverage"
echo "  ✅ 單元測試全數通過 (All unit tests passed)"

# 4. 執行多階段 Dockerfile 建構驗證
echo -e "\n[階段 4/4] 驗證 Docker 映像檔多階段構建 (Docker Multi-stage Build)..."
$DOCKER_CMD build -t orderservice:ci-test -f src/OrderService/Dockerfile .
echo "  ✅ Docker 映像檔構建成功 (Docker build verified)"

# 清理產出目錄
rm -rf src/OrderService/bin src/OrderService/obj \
       tests/OrderService.Tests/bin tests/OrderService.Tests/obj \
       tests/OrderService.Tests/TestResults

echo -e "\n================================================================"
echo " 🎉 恭喜！本地標準 CI 流水線所有檢查與測試均已通過！"
echo "================================================================"
