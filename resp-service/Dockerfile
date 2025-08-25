# 使用官方的 Go 运行时作为父镜像
FROM golang:1.25.0-trixie
LABEL author="HeChu"

# 设置工作目录
WORKDIR /app

# 将 go.mod 和 go.sum 文件复制到工作目录
COPY go.mod go.sum ./

# 下载依赖
RUN go mod download

# 将源代码复制到工作目录
COPY . .

# 构建 Go 应用，禁用 CGO 并指定目标操作系统为 Linux
RUN CGO_ENABLED=0 GOOS=linux go build -o /hddis-service

# 暴露你的 Go 服务运行的端口
# 请将 8080 替换为你的服务实际使用的端口
EXPOSE 6060
EXPOSE 6379

# 定义容器启动时执行的命令
CMD [ "/hddis-service" ]