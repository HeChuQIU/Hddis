# 设置错误时退出
$ErrorActionPreference = "Stop"

# 1. 创建registry容器（如果不存在）
$reg_name = 'kind-registry'
$reg_port = '6000'

# 检查容器是否已存在并在运行
#try {
#  $registryStatus = docker inspect -f '{{.State.Running}}' $reg_name 2>&1
#  if ($registryStatus -ne 'true') {
#    # 容器存在但未运行，先删除再创建
#    docker rm -f $reg_name 2>$null
#    docker run `
#            -d --restart=always -p "127.0.0.1:${reg_port}:5000" --network bridge --name $reg_name `
#            registry:2
#  }
#} catch {
#  # 容器不存在，创建新容器
#  docker run `
#        -d --restart=always -p "127.0.0.1:${reg_port}:5000" --network bridge --name $reg_name `
#        registry:2
#}

# 2. 创建kind集群并启用containerd registry配置目录
$kindConfig = @"
kind: Cluster
apiVersion: kind.x-k8s.io/v1alpha4
containerdConfigPatches:
- |-
  [plugins."io.containerd.grpc.v1.cri".registry]
    config_path = "/etc/containerd/certs.d"
"@

# 将配置保存到临时文件
$tempFile = [System.IO.Path]::GetTempFileName()
$kindConfig | Out-File -FilePath $tempFile -Encoding ASCII

# 使用配置文件创建集群
kind create cluster --config=$tempFile

# 删除临时文件
Remove-Item $tempFile

# 3. 将registry配置添加到节点
$REGISTRY_DIR = "/etc/containerd/certs.d/localhost:${reg_port}"
$nodes = kind get nodes

foreach ($node in $nodes) {
  # 在节点上创建目录
  docker exec $node mkdir -p $REGISTRY_DIR

  # 创建hosts.toml配置文件
  $hostsToml = @"
[host."http://${reg_name}:5000"]
"@

  # 将配置复制到节点
  $hostsToml | docker exec -i $node cp /dev/stdin "${REGISTRY_DIR}/hosts.toml"
}

# 4. 将registry连接到集群网络（如果尚未连接）
$networkInfo = docker inspect -f='{{json .NetworkSettings.Networks.kind}}' $reg_name 2>$null
if ($networkInfo -eq 'null') {
  docker network connect "kind" $reg_name
}

# 5. 记录本地registry信息
$configMapYaml = @"
apiVersion: v1
kind: ConfigMap
metadata:
  name: local-registry-hosting
  namespace: kube-public
data:
  localRegistryHosting.v1: |
    host: "localhost:${reg_port}"
    help: "https://kind.sigs.k8s.io/docs/user/local-registry/"
"@

# 应用ConfigMap
$configMapYaml | kubectl apply -f -

Write-Host "Kind集群和本地registry设置完成!"