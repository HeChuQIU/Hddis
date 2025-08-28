package main

import (
	"net/http"

	"github.com/dapr/components-contrib/middleware"
)

type hashSlot struct {
}

func (m *hashSlot) GetHandler(metadata middleware.Metadata) (func(next http.Handler) http.Handler, error) {
	var err error
	return func(next http.Handler) http.Handler {
		return http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
			// 入站逻辑
			// ...

			// 调用下一个处理器
			next.ServeHTTP(w, r)

			// 出站逻辑
			// ...
		}
	}, err
}
